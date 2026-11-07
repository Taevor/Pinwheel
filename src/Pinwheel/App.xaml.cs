using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Pinwheel;

public partial class App : System.Windows.Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Pinwheel";
    private const string PipeName = "PinwheelNotificationPipe";
    private const int VkControl = 0x11;
    private const int VkRightButton = 0x02;
    private const int VkMiddleButton = 0x04;
    private const uint MouseEventfMiddleDown = 0x0020;
    private const uint MouseEventfMiddleUp = 0x0040;
    private const uint MouseEventfRightDown = 0x0008;
    private const uint MouseEventfRightUp = 0x0010;

    private AppConfig? _config;
    private RadialWindow? _radial;
    private GlobalMouseHook? _hook;
    private KeyboardHook? _keyboardHook;
    private Forms.NotifyIcon? _tray;
    private SettingsWindow? _settings;
    private System.Windows.Threading.DispatcherTimer? _showTimer;
    private System.Windows.Threading.DispatcherTimer? _watchdog;
    private DateTime? _watchdogReleasedAt;
    private System.Windows.Point? _pressPoint;
    private bool _sequence;
    private bool _cancelPending;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [CRASH] {args.ExceptionObject}\n"); } catch { }
        };
        DispatcherUnhandledException += (s, args) =>
        {
            try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [CRASH-Dispatcher] {args.Exception}\n"); } catch { }
        };

        try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [STARTUP] args={string.Join(" ", e.Args)}\n"); } catch { }

        base.OnStartup(e);

        // 处理外部/脚本命令行通知调用：Pinwheel.exe notify <title> [message] [type]
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "notify", StringComparison.OrdinalIgnoreCase))
        {
            HandleNotifyCommand(e.Args);
            return;
        }

        // 处理外部/脚本命令行翻译调用：Pinwheel.exe translate [text]
        if (e.Args.Length >= 1 && string.Equals(e.Args[0], "translate", StringComparison.OrdinalIgnoreCase))
        {
            HandleTranslateCommand(e.Args);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _config = ConfigStore.Load();
        _radial = new RadialWindow();
        MainWindow = _radial;
        ActionRunner.OnError += ShowError;
        CreateTrayIcon();
        ApplyAutoStart();
        StartNotificationPipeServer();

        _hook = new GlobalMouseHook();
        _hook.ButtonDown += OnButtonDown;
        _hook.ButtonUp += OnButtonUp;
        _hook.Start();

        _keyboardHook = new KeyboardHook();
        _keyboardHook.EscapePressed += CancelFromKeyboard;
        _keyboardHook.Start();
    }

    private void HandleNotifyCommand(string[] args)
    {
        string title = args.Length >= 2 ? args[1] : "Pinwheel";
        string message = args.Length >= 3 ? args[2] : "";
        string typeStr = args.Length >= 4 ? args[3] : "success";
        var type = typeStr.ToLowerInvariant() switch
        {
            "error" => NotificationType.Error,
            "info" => NotificationType.Info,
            _ => NotificationType.Success
        };

        try
        {
            // 尝试连接已有主进程管道发送通知
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine($"{title}\t{message}\t{typeStr}");
            writer.Flush();
            Shutdown();
        }
        catch
        {
            // 主进程未运行时，以轻量方式直接弹出后关闭
            NotificationService.Show(title, message, type);
            _ = Task.Delay(3500).ContinueWith(_ => Dispatcher.Invoke(Shutdown));
        }
    }

    private void HandleTranslateCommand(string[] args)
    {
        string text = args.Length >= 2 ? string.Join(" ", args.Skip(1)) : "";
        try
        {
            // 尝试连接已有主进程管道发送翻译指令
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine($"CMD:TRANSLATE\t{text}");
            writer.Flush();
            Shutdown();
        }
        catch
        {
            // 主进程未运行时，在当前实例中直接拉起翻译窗口
            Dispatcher.Invoke(() =>
            {
                TranslationWindow.ShowOrTranslate(text);
            });
        }
    }

    private static void StartNotificationPipeServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string? line = await reader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (line.StartsWith("CMD:TRANSLATE\t"))
                        {
                            string text = line.Substring("CMD:TRANSLATE\t".Length);
                            Current.Dispatcher.Invoke(() =>
                            {
                                TranslationWindow.ShowOrTranslate(text);
                            });
                            continue;
                        }

                        var parts = line.Split('\t');
                        string title = parts.Length > 0 ? parts[0] : "Pinwheel";
                        string msg = parts.Length > 1 ? parts[1] : "";
                        string typeStr = parts.Length > 2 ? parts[2] : "success";
                        var type = typeStr.ToLowerInvariant() switch
                        {
                            "error" => NotificationType.Error,
                            "info" => NotificationType.Info,
                            _ => NotificationType.Success
                        };
                        NotificationService.Show(title, msg, type);
                    }
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
    }

    private void OnButtonDown(MouseKey key)
    {
        Dispatcher.Invoke(() =>
        {
            if (_config is null || _radial is null || _hook is null)
            {
                return;
            }

            bool isSelf = ForegroundWindow.IsCurrentProcess();
            string? processName = isSelf ? "Pinwheel.exe" : ForegroundWindow.GetProcessName();
            if (!isSelf && processName is not null && processName.StartsWith("Pinwheel", StringComparison.OrdinalIgnoreCase))
            {
                isSelf = true;
            }

            var profile = processName is null
                ? null
                : _config.Apps.FirstOrDefault(app =>
                    app.Enabled && string.Equals(app.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

            // 如果不是已启用的配置软件，且不是本软件自身，则不触发
            if (profile is null && !isSelf)
            {
                _sequence = false;
                _hook.SwallowDownKey = MouseKey.None;
                return;
            }

            string triggerMode = isSelf ? _config.TriggerMode : (profile?.TriggerMode ?? _config.TriggerMode);
            if (!IsTriggerKeyDown(key, triggerMode))
            {
                return;
            }

            if (!isSelf && (profile?.DisableOnFullscreen ?? true) && IsFullscreenWindow())
            {
                _sequence = false;
                _hook.SwallowDownKey = MouseKey.None;
                return;
            }

            _sequence = true;
            _hook.SwallowDownKey = key;

            GetCursorPos(out GlobalMouseHook.POINT pressed);
            _pressPoint = new System.Windows.Point(pressed.X, pressed.Y);

            _showTimer?.Stop();
            _showTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_config.HoldMilliseconds)
            };
            _showTimer.Tick += (_, _) =>
            {
                _showTimer.Stop();

                if (_pressPoint is not { } start)
                {
                    return;
                }

                GetCursorPos(out GlobalMouseHook.POINT now);
                double dx = now.X - start.X;
                double dy = now.Y - start.Y;
                int threshold = _config.MoveThresholdPixels;
                if (dx * dx + dy * dy > threshold * threshold)
                {
                    return;
                }

                // 在本软件自身触发时，只展示 8 个通用指令，其余外圈留空供即时测试
                IReadOnlyList<RadialAction> appActions = isSelf || profile is null
                    ? Array.Empty<RadialAction>()
                    : profile.Actions;

                _radial.ShowAt(
                    new System.Windows.Point(now.X, now.Y),
                    _config.GlobalActions,
                    appActions,
                    _config.RadialSize,
                    _config.Theme);
                if (_keyboardHook is not null)
                {
                    _keyboardHook.SwallowEscape = true;
                }

                _watchdog?.Stop();
                _watchdog = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _watchdogReleasedAt = null;
                _watchdog.Tick += (_, _) =>
                {
                    if (_radial is null || !_radial.IsVisible)
                    {
                        _watchdog.Stop();
                        _watchdogReleasedAt = null;
                        return;
                    }

                    if (IsTriggerButtonStillDown(key))
                    {
                        _watchdogReleasedAt = null;
                        return;
                    }

                    DateTime now = DateTime.UtcNow;
                    _watchdogReleasedAt ??= now;
                    if ((now - _watchdogReleasedAt.Value).TotalSeconds >= 10)
                    {
                        _watchdog.Stop();
                        _radial.HideRadial();
                        _sequence = false;
                        _watchdogReleasedAt = null;
                        if (_keyboardHook is not null)
                        {
                            _keyboardHook.SwallowEscape = false;
                        }
                    }
                };
                _watchdog.Start();
            };
            _showTimer.Start();
        });
    }

    private void OnButtonUp(MouseKey key)
    {
        Dispatcher.Invoke(() =>
        {
            _showTimer?.Stop();
            _watchdog?.Stop();
            _watchdogReleasedAt = null;

            if (!_sequence)
            {
                return;
            }

            _sequence = false;
            if (_hook is not null)
            {
                _hook.SwallowUpKey = key;
            }
            if (_keyboardHook is not null)
            {
                _keyboardHook.SwallowEscape = false;
            }

            if (_cancelPending)
            {
                _cancelPending = false;
                return;
            }

            if (_radial is not null && _radial.IsVisible)
            {
                GetCursorPos(out GlobalMouseHook.POINT point);
                var cursor = new System.Windows.Point(point.X, point.Y);
                var action = _radial.GetActionAt(cursor);
                _radial.HideRadial();

                if (action is not null)
                {
                    _ = RunActionAndFlashAsync(action, cursor);
                }
            }
            else
            {
                SimulateClick(key);
            }
        });
    }

    private async Task RunActionAndFlashAsync(RadialAction action, System.Windows.Point cursor)
    {
        bool ok = await ActionRunner.RunAsync(action);
        if (!ok && _radial is not null)
        {
            _radial.FlashError(cursor);
        }
    }

    private void CancelFromKeyboard()
    {
        Dispatcher.Invoke(() =>
        {
            if (_radial is null || !_radial.IsVisible)
            {
                return;
            }

            _cancelPending = true;
            _radial.HideRadial();
            if (_keyboardHook is not null)
            {
                _keyboardHook.SwallowEscape = false;
            }
        });
    }

    private static bool IsTriggerKeyDown(MouseKey key, string mode)
    {
        return mode switch
        {
            "Middle" => key == MouseKey.Middle,
            "Right" => key == MouseKey.Right,
            "CtrlMiddle" => key == MouseKey.Middle && IsCtrlDown(),
            _ => key == MouseKey.Middle && IsCtrlDown()
        };
    }

    private static bool IsCtrlDown()
    {
        return (GetKeyState(VkControl) & 0x8000) != 0;
    }

    private static bool IsTriggerButtonStillDown(MouseKey key)
    {
        return key switch
        {
            MouseKey.Middle => IsKeyDown(VkMiddleButton),
            MouseKey.Right => IsKeyDown(VkRightButton),
            _ => false
        };
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private void CreateTrayIcon()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Pinwheel",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            Font = new System.Drawing.Font("Segoe UI", 9f),
            ShowImageMargin = false
        };
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.MouseDoubleClick += (_, _) => OpenSettings();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
            }
        }
        catch
        {
            // 忽略，使用默认图标。
        }

        return SystemIcons.Application;
    }

    private void OpenSettings()
    {
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_config!, OnSettingsChanged);
        _settings.Show();
    }

    private void OnSettingsChanged()
    {
        ApplyAutoStart();
    }

    private void ApplyAutoStart()
    {
        if (_config is not null)
        {
            SetAutoStart(_config.AutoStart);
        }
    }

    private void ShowError(string message)
    {
        if (_config?.ShowErrorBalloon == true)
        {
            NotificationService.ShowError("Pinwheel", message);
        }
    }

    private static void SimulateClick(MouseKey key)
    {
        if (key == MouseKey.Middle)
        {
            mouse_event(MouseEventfMiddleDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventfMiddleUp, 0, 0, 0, UIntPtr.Zero);
        }
        else if (key == MouseKey.Right)
        {
            mouse_event(MouseEventfRightDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventfRightUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    private static bool IsFullscreenWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW")
        {
            return false;
        }

        int style = GetWindowLong(hwnd, GwlStyle);
        if ((style & WsCaption) != 0)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out RECT rect))
        {
            return false;
        }

        var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
        if (primaryScreen is null)
        {
            return false;
        }

        var screen = primaryScreen.Bounds;
        return rect.Left <= screen.Left && rect.Top <= screen.Top
            && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        string logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
        try { File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} [EXIT] OnExit called, exitCode={e.ApplicationExitCode}\n{Environment.StackTrace}\n"); } catch { }
        _hook?.Dispose();
        _keyboardHook?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out GlobalMouseHook.POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed class FluentMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    public FluentMenuRenderer() : base(new FluentColorTable()) { }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            var rect = new System.Drawing.Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xEB, 0xF3, 0xFC));
            e.Graphics.FillRectangle(brush, rect);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xC5, 0xDC, 0xFA));
            e.Graphics.DrawRectangle(pen, rect);
        }
        else
        {
            base.OnRenderMenuItemBackground(e);
        }
    }
}

internal sealed class FluentColorTable : Forms.ProfessionalColorTable
{
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.White;
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(0xE5, 0xE5, 0xE5);
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
    public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(0xEE, 0xEE, 0xEE);
    public override System.Drawing.Color SeparatorLight => System.Drawing.Color.White;
}
