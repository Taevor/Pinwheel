using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Pinwheel;

public sealed class ActionPackageManifest
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Type { get; set; }
    public string Target { get; set; } = "";
    public string Process { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ActionPackage
{
    private static readonly Dictionary<string, string> KnownProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EXCEL.EXE"] = "Excel",
        ["CORELDRW.EXE"] = "Coreldraw",
        ["WINWORD.EXE"] = "Word",
        ["POWERPNT.EXE"] = "Powerpoint",
        ["MSPAINT.EXE"] = "Paint",
        ["NOTEPAD.EXE"] = "Notepad",
        ["WPS.EXE"] = "Wps",
        ["ET.EXE"] = "Et"
    };

    public string DirectoryPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public ActionType Type { get; set; }
    public string Target { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Description { get; set; } = "";
    public object? SoftwareIcon { get; set; }
    public System.Windows.Media.SolidColorBrush? ThemePrimaryBrush { get; set; }
    public System.Windows.Media.SolidColorBrush? ThemeLightBrush { get; set; }
    public System.Windows.Media.SolidColorBrush? ThemeBorderBrush { get; set; }

    public static string FormatProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "全局通用";
        }

        if (KnownProcessNames.TryGetValue(processName.Trim(), out string? known))
        {
            return known;
        }

        string baseName = Path.GetFileNameWithoutExtension(processName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return processName;
        }

        return char.ToUpperInvariant(baseName[0]) + baseName.Substring(1).ToLowerInvariant();
    }

    public string SoftwareName => FormatProcessName(ProcessName);

    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName)
        ? Name
        : $"{Name} {SoftwareName}";
}

public static class ActionStore
{
    public static string ActionDirectory => Path.Combine(AppContext.BaseDirectory, "actions");

    public static List<ActionPackage> LoadAll()
    {
        var result = new List<ActionPackage>();
        if (!Directory.Exists(ActionDirectory))
        {
            return result;
        }

        foreach (var directory in Directory.GetDirectories(ActionDirectory))
        {
            try
            {
                var package = ReadPackage(directory);
                if (package is not null)
                {
                    result.Add(package);
                }
            }
            catch (Exception ex)
            {
                Log($"LoadAll 跳过: {directory}, 错误: {ex.Message}");
            }
        }

        return result;
    }

    public static ActionPackage? ReadPackage(string directory)
    {
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var manifest = JsonSerializer.Deserialize<ActionPackageManifest>(File.ReadAllText(manifestPath), options);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name))
        {
            return null;
        }

        var type = (ActionType)Math.Clamp(manifest.Type, 0, 5);
        string target = manifest.Target;
        if (type == ActionType.RunScript)
        {
            target = Path.Combine(directory, target);
        }

        return new ActionPackage
        {
            DirectoryPath = directory,
            Name = manifest.Name,
            Icon = manifest.Icon,
            Type = type,
            Target = target,
            ProcessName = manifest.Process ?? "",
            Description = manifest.Description ?? ""
        };
    }

    public static void Install(string sourceZipPath)
    {
        Directory.CreateDirectory(ActionDirectory);
        string name = Path.GetFileNameWithoutExtension(sourceZipPath);
        string destination = Path.Combine(ActionDirectory, name);

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }

        ZipFile.ExtractToDirectory(sourceZipPath, destination);
    }

    public static void Uninstall(ActionPackage package)
    {
        if (Directory.Exists(package.DirectoryPath))
        {
            Directory.Delete(package.DirectoryPath, true);
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
