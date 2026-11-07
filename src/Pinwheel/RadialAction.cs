using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pinwheel;

public enum ActionType
{
    OpenProgram,
    OpenUrl,
    OpenFolder,
    SendHotkey,
    SendText,
    RunScript,
    Composite
}

public enum TriggerMode
{
    Middle,
    Right,
    CtrlMiddle
}

public sealed record RadialAction(string Name, ActionType Type, string Target, string Icon)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RadialAction>? Steps { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DelayMs { get; init; }

    /// <summary>
    /// 判断是否为空白/未配置动作
    /// </summary>
    [JsonIgnore]
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Target) &&
        (Steps == null || Steps.Count == 0) &&
        (string.IsNullOrWhiteSpace(Name) || Name == "空白" || Name.StartsWith("格子 "));

    /// <summary>
    /// 默认的空白占位动作
    /// </summary>
    public static RadialAction Empty => new("", ActionType.OpenProgram, "", "");
}

public sealed class AppProfile
{
    public string ProcessName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool? DisableOnFullscreen { get; set; }
    public string? TriggerMode { get; set; }
    public List<RadialAction> Actions { get; set; } = new();
}

public static class DefaultGlobalActions
{
    public static IReadOnlyList<RadialAction> Create() =>
    [
        new RadialAction("划词翻译", ActionType.RunScript, "actions\\通用-划词翻译\\translate.ps1", "🌐"),
        new RadialAction("复制", ActionType.SendHotkey, "^c", "📋"),
        new RadialAction("粘贴", ActionType.SendHotkey, "^v", "📌"),
        new RadialAction("剪切", ActionType.SendHotkey, "^x", "✂️"),
        new RadialAction("全选", ActionType.SendHotkey, "^a", "☑️"),
        new RadialAction("撤销", ActionType.SendHotkey, "^z", "↩️"),
        new RadialAction("保存", ActionType.SendHotkey, "^s", "💾"),
        new RadialAction("关闭窗口", ActionType.SendHotkey, "%{F4}", "❌")
    ];
}

public static class DefaultAppActions
{
    public static IReadOnlyList<RadialAction> Create() => [];
}
