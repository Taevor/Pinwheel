using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pinwheel;

public sealed class AppConfig
{
    public string TriggerMode { get; set; } = "CtrlMiddle";
    public int HoldMilliseconds { get; set; } = 200;
    public int MoveThresholdPixels { get; set; } = 30;
    public bool AutoStart { get; set; }
    public bool ShowErrorBalloon { get; set; } = true;
    public string RadialSize { get; set; } = "380";
    public string Theme { get; set; } = "LightBlue";
    public List<RadialAction> GlobalActions { get; set; } = new();
    public List<AppProfile> Apps { get; set; } = new();
    public TranslationConfig Translation { get; set; } = new();
}

public static class ConfigStore
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");
    public static string BackupDirectory => Path.Combine(AppContext.BaseDirectory, "backups");
    private const int MaxBackups = 20;

    public static AppConfig Load()
    {
        return LoadFrom(ConfigPath);
    }

    public static AppConfig LoadFrom(string path)
    {
        AppConfig config;
        try
        {
            if (File.Exists(path))
            {
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
            }
            else
            {
                config = new AppConfig();
            }
        }
        catch
        {
            config = new AppConfig();
        }

        return Normalize(config);
    }

    public static void Save(AppConfig config)
    {
        BackupCurrent();
        SaveTo(config, ConfigPath);
    }

    public static void SaveTo(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static void BackupCurrent()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            Directory.CreateDirectory(BackupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string destination = Path.Combine(BackupDirectory, $"config-{stamp}.json");
            File.Copy(ConfigPath, destination, true);
            PruneBackups();
        }
        catch
        {
            // 备份失败不影响保存。
        }
    }

    private static void PruneBackups()
    {
        var files = Directory.GetFiles(BackupDirectory, "config-*.json").OrderByDescending(f => f).ToList();
        while (files.Count > MaxBackups)
        {
            File.Delete(files[^1]);
            files.RemoveAt(files.Count - 1);
        }
    }

    public static List<string> ListBackups()
    {
        try
        {
            if (!Directory.Exists(BackupDirectory))
            {
                return new List<string>();
            }

            return Directory.GetFiles(BackupDirectory, "config-*.json").OrderByDescending(f => f).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static AppConfig Normalize(AppConfig config)
    {
        if (config.TriggerMode is not ("Middle" or "Right" or "CtrlMiddle")) config.TriggerMode = "CtrlMiddle";
        if (config.HoldMilliseconds < 0 || config.HoldMilliseconds > 2000) config.HoldMilliseconds = 200;
        if (config.MoveThresholdPixels < 5 || config.MoveThresholdPixels > 200) config.MoveThresholdPixels = 30;
        config.RadialSize = config.RadialSize switch
        {
            "Small" => "320",
            "Medium" => "380",
            "Large" => "440",
            "ExtraLarge" => "510",
            "Max" => "590",
            _ => config.RadialSize
        };
        if (!double.TryParse(config.RadialSize, out double size) || size < 200 || size > 1000)
        {
            config.RadialSize = "380";
        }
        if (config.Theme is not ("LightBlue" or "White" or "Gray" or "Glass" or "Dark")) config.Theme = "LightBlue";

        config.GlobalActions ??= new List<RadialAction>();
        if (config.GlobalActions.Count == 0) config.GlobalActions = DefaultGlobalActions.Create().ToList();

        config.Apps ??= new List<AppProfile>();
        foreach (var app in config.Apps)
        {
            app.ProcessName ??= "";
            app.Actions ??= new List<RadialAction>();
        }

        config.Translation ??= new TranslationConfig();

        return config;
    }
}

public sealed class TranslationConfig
{
    public string Provider { get; set; } = "DeepSeek"; // DeepSeek, SiliconFlow, OpenAI, Custom, Free
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string Model { get; set; } = "deepseek-chat";
    public bool Topmost { get; set; } = true;
    public double WindowWidth { get; set; } = 480;
    public double WindowHeight { get; set; } = 560;
}
