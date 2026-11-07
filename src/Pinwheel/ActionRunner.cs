using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pinwheel;

public static class ActionRunner
{
    public static event Action<string>? OnError;

    public static async Task<bool> RunAsync(RadialAction action)
    {
        try
        {
            Log($"执行指令: {action.Name}, 类型={action.Type}, 目标={action.Target}, 前台={ForegroundWindow.GetProcessName() ?? "null"}");

            bool ok = await ExecuteAsync(action);
            Log($"执行完成: {action.Name}");
            return ok;
        }
        catch (Exception ex)
        {
            Log($"执行失败: {action.Name}, {ex.Message}");
            OnError?.Invoke($"指令「{action.Name}」执行失败：{ex.Message}");
            return false;
        }
    }

    private static async Task<bool> ExecuteAsync(RadialAction action)
    {
        if (action.Type == ActionType.Composite)
        {
            var steps = action.Steps ?? new List<RadialAction>();
            foreach (var step in steps)
            {
                bool ok = await ExecuteAsync(step);
                if (!ok)
                {
                    return false;
                }

                if (action.DelayMs > 0)
                {
                    await Task.Delay(action.DelayMs);
                }
            }

            return true;
        }

        switch (action.Type)
        {
            case ActionType.OpenProgram:
            case ActionType.OpenUrl:
                Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });
                break;

            case ActionType.OpenFolder:
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{action.Target}\"") { UseShellExecute = true });
                break;

            case ActionType.SendHotkey:
                ReleaseModifierKeys();
                SendKeys.SendWait(action.Target);
                ReleaseModifierKeys();
                break;

            case ActionType.SendText:
                System.Windows.Clipboard.SetText(action.Target);
                SendKeys.SendWait("^v");
                break;

            case ActionType.RunScript:
                await RunScriptWithNotificationAsync(action);
                break;

            default:
                throw new InvalidOperationException($"不支持的指令类型: {action.Type}");
        }

        return true;
    }

    private static async Task RunScriptWithNotificationAsync(RadialAction action)
    {
        string targetPath = action.Target?.Trim() ?? "";

        // 预先检查脚本文件是否存在（避免已卸载指令包报莫名其妙的 PowerShell 异常）
        if (string.IsNullOrWhiteSpace(targetPath) || !System.IO.File.Exists(targetPath))
        {
            string fileName = string.IsNullOrWhiteSpace(targetPath) ? "" : System.IO.Path.GetFileName(targetPath);
            string errorDetail = string.IsNullOrWhiteSpace(targetPath)
                ? "脚本目标路径为空，请在设置中配置有效脚本。"
                : $"指令包已被卸载或脚本文件不存在：{fileName}";

            Log($"[ActionRunner] 指令包脚本不存在: {action.Name}, 路径={targetPath}");
            NotificationService.ShowError(action.Name, errorDetail);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '{targetPath}'\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // 注入 Pinwheel 主程序环境变量，便于脚本需要时直接调用
            psi.EnvironmentVariables["PINWHEEL_APP"] = Environment.ProcessPath ?? "";

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Log($"[ActionRunner] 无法启动 PowerShell 进程: {action.Name}");
                NotificationService.ShowError(action.Name, "无法启动 PowerShell 进程");
                return;
            }

            int pid = proc.Id;
            Log($"[ActionRunner] 脚本已启动: PID={pid}, 指令={action.Name}, 目标={action.Target}");

            var stdoutTask = ReadStreamAutoEncodingAsync(proc.StandardOutput.BaseStream);
            var stderrTask = ReadStreamAutoEncodingAsync(proc.StandardError.BaseStream);

            // 超时看门狗：最长等待 15 秒，避免外部脚本死循环挂死主程序或占用 CPU
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                Log($"[ActionRunner] 脚本执行超时 (15秒): PID={pid}, 指令={action.Name}，正在强制终止进程树...");
                try { proc.Kill(entireProcessTree: true); } catch { }
                NotificationService.ShowError(action.Name, "指令执行超时 (15秒)，已自动强制终止。");
                return;
            }

            sw.Stop();
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            Log($"[ActionRunner] 脚本已退出: PID={pid}, ExitCode={proc.ExitCode}, 耗时={sw.ElapsedMilliseconds}ms");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Log($"[ActionRunner] [PID:{pid} 详细诊断日志]{Environment.NewLine}{stdout.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Log($"[ActionRunner] [PID:{pid} 错误输出日志]{Environment.NewLine}{stderr.Trim()}");
            }

            if (proc.ExitCode == 0)
            {
                string summary = ExtractMeaningfulLine(stdout);
                if (summary.Contains("#silent") || summary.Contains("划词翻译已就绪"))
                {
                    Log($"[ActionRunner] 指令执行完成 (静默通知): {action.Name}");
                    return;
                }
                if (string.IsNullOrWhiteSpace(summary))
                {
                    summary = "指令执行完成";
                }
                NotificationService.ShowSuccess(action.Name, summary, action.Icon);
                Log($"[ActionRunner] 执行成功通知: {action.Name}, 输出={summary}");
            }
            else
            {
                string error = ExtractMeaningfulLine(stderr);
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = ExtractMeaningfulLine(stdout);
                }
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"执行失败 (退出码: {proc.ExitCode})";
                }
                NotificationService.ShowError(action.Name, error);
                Log($"[ActionRunner] 执行失败通知: {action.Name}, 错误={error}");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log($"[ActionRunner] 脚本运行异常: {action.Name}, 耗时={sw.ElapsedMilliseconds}ms, 异常={ex.Message}");
            NotificationService.ShowError(action.Name, ex.Message);
        }
    }

    private static async Task<string> ReadStreamAutoEncodingAsync(System.IO.Stream stream)
    {
        using var ms = new System.IO.MemoryStream();
        await stream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();
        if (bytes.Length == 0) return string.Empty;

        try
        {
            // 优先尝试严格 UTF-8，遇到非法字节抛出异常
            var utf8Strict = new System.Text.UTF8Encoding(false, true);
            return utf8Strict.GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            // 回退到系统本地默认编码（GBK / CP936）
            return System.Text.Encoding.Default.GetString(bytes);
        }
    }

    private static string ExtractMeaningfulLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var usefulLines = lines
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l)
                     && !l.StartsWith("+")
                     && !l.StartsWith("~~~")
                     && !l.StartsWith("#")
                     && !l.StartsWith("Write-Diag")
                     && !l.StartsWith("At line:")
                     && !l.StartsWith("At ")
                     && !l.StartsWith("所在位置")
                     && !l.StartsWith("Position:")
                     && !l.Contains("CategoryInfo")
                     && !l.Contains("FullyQualifiedErrorId")
                     && !l.StartsWith("===")
                     && !l.StartsWith("{")
                     && !l.StartsWith("}")
                     && !l.StartsWith("\"")
                     && !l.StartsWith("[AI")
                     && !l.StartsWith("[20"))
            .ToList();

        if (usefulLines.Count == 0)
        {
            var fallback = lines.Select(l => l.Trim()).LastOrDefault(l => !l.StartsWith("=") && !string.IsNullOrEmpty(l));
            return fallback ?? "";
        }

        // 优先检查是否有明确的脚本错误信息行（形如：*.ps1 : 实际错误内容）
        var errorLineWithColon = usefulLines.FirstOrDefault(l => l.Contains(" : ") || l.Contains(".ps1:"));
        string targetLine = errorLineWithColon ?? usefulLines[^1];

        int colonIdx = targetLine.IndexOf(" : ", StringComparison.Ordinal);
        if (colonIdx >= 0 && colonIdx + 3 < targetLine.Length)
        {
            targetLine = targetLine.Substring(colonIdx + 3).Trim();
        }

        return targetLine;
    }

    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void ReleaseModifierKeys()
    {
        keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkShift, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkLwin, 0, KeyeventfKeyup, UIntPtr.Zero);
        keybd_event(VkRwin, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    private const byte VkMenu = 0x12;
    private const byte VkControl = 0x11;
    private const byte VkShift = 0x10;
    private const byte VkLwin = 0x5B;
    private const byte VkRwin = 0x5C;
    private const uint KeyeventfKeyup = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
