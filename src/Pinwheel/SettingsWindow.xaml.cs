using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Pinwheel;

public sealed record RunningApp(string Name, ImageSource? Icon);

public sealed record SceneItem(string Name, ImageSource? Icon, string Emoji, AppProfile? Profile);

public sealed class SoftwareCategory
{
    public string Name { get; set; } = "";
    public ImageSource? Icon { get; set; }
    public string ProcessName { get; set; } = "";
    public List<ActionPackage> Packages { get; } = new();
    public int ItemCount => Packages.Count;

    public SolidColorBrush PrimaryBrush { get; set; } = Brushes.DodgerBlue;
    public SolidColorBrush LightBackgroundBrush { get; set; } = Brushes.AliceBlue;
    public SolidColorBrush BorderBrush { get; set; } = Brushes.LightSkyBlue;
}

public partial class SettingsWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly Action _onChanged;
    private bool _loading;
    private int _sceneIndex = -1;
    private AppProfile? _selectedApp;
    private int _slotIndex = -1;
    private List<ActionPackage> _packages = new();
    private List<SoftwareCategory> _categories = new();
    private SoftwareCategory? _selectedCategory;
    private bool _isCategoryExpanded = true;
    private int _gridColumns = 4;

    public int GridColumns
    {
        get => _gridColumns;
        set
        {
            if (_gridColumns != value)
            {
                _gridColumns = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(GridColumns)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private List<RadialAction> _editingSteps = new();
    private int _editingDelayMs = 250;
    private bool _loadingSteps;
    private int _dragFromIndex = -1;
    private Point _dragStartPoint;
    private bool _dragging;
    private bool _loadingEditor;
    private string _searchText = "";
    private static readonly Dictionary<string, ImageSource?> ProcessIconCache = new(StringComparer.OrdinalIgnoreCase);

    public SettingsWindow(AppConfig config, Action onChanged)
    {
        _config = config;
        _onChanged = onChanged;
        InitializeComponent();

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (ver != null)
        {
            Title = $"Pinwheel 设置 v{ver.Major}.{ver.Minor}.{ver.Build}";
            AboutVersionText.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
        }

        SlotLayoutBox.ItemsSource = new[] { "两圈", "一圈" };
        SlotLayoutBox.SelectedIndex = 0;

        AppTriggerModeBox.ItemsSource = new[] { "跟随全局", "中键", "右键", "Ctrl+中键" };

        TypeBox.ItemsSource = new[]
        {
            "打开程序",
            "打开网址",
            "打开文件夹",
            "模拟快捷键",
            "发送文本",
            "运行脚本",
            "组合指令"
        };

        StepTypeBox.ItemsSource = new[]
        {
            "打开程序",
            "打开网址",
            "打开文件夹",
            "模拟快捷键",
            "发送文本",
            "运行脚本"
        };
        StepTypeBox.SelectedIndex = 0;

        AutoCleanOrphanActions();
        LoadGeneralSettings();
        RefreshRunningApps();
        RefreshPackages();
        ReloadScenes();
        ReloadCurrentSlots();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        EnableWindows11FluentEffects();
    }

    private void EnableWindows11FluentEffects()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // Windows 11 圆角 (DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)
            int corner = 2;
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

            // Windows 11 云母材质 (DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_MAINWINDOW = 2)
            int backdrop = 2;
            DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        }
        catch
        {
            // 旧版系统回退
        }
    }

    private static (Color primary, Color lightBg, Color border) GetSoftwareThemeColors(string softwareName, string processName)
    {
        string key = (softwareName + " " + processName).ToLowerInvariant();

        if (key.Contains("excel"))
        {
            return (Color.FromRgb(0x10, 0x7C, 0x41), Color.FromRgb(0xEB, 0xF7, 0xF0), Color.FromRgb(0xA6, 0xDE, 0xC0));
        }
        if (key.Contains("word") || key.Contains("winword"))
        {
            return (Color.FromRgb(0x18, 0x5A, 0xBD), Color.FromRgb(0xEB, 0xF2, 0xFC), Color.FromRgb(0xB2, 0xD1, 0xF8));
        }
        if (key.Contains("powerpnt") || key.Contains("powerpoint"))
        {
            return (Color.FromRgb(0xD8, 0x3B, 0x01), Color.FromRgb(0xFD, 0xF0, 0xEA), Color.FromRgb(0xF8, 0xC3, 0xAC));
        }
        if (key.Contains("illustrator"))
        {
            return (Color.FromRgb(0xE6, 0x78, 0x00), Color.FromRgb(0xFF, 0xF5, 0xE5), Color.FromRgb(0xFF, 0xDC, 0x99));
        }
        if (key.Contains("photoshop"))
        {
            return (Color.FromRgb(0x00, 0x86, 0xD4), Color.FromRgb(0xEC, 0xF6, 0xFD), Color.FromRgb(0xAF, 0xDC, 0xFB));
        }
        if (key.Contains("coreldrw") || key.Contains("coreldraw"))
        {
            return (Color.FromRgb(0x00, 0x8C, 0x3A), Color.FromRgb(0xEA, 0xF7, 0xF0), Color.FromRgb(0x9E, 0xDF, 0xBA));
        }
        if (key.Contains("everything"))
        {
            return (Color.FromRgb(0x73, 0x38, 0xA0), Color.FromRgb(0xF5, 0xEF, 0xFA), Color.FromRgb(0xD8, 0xBE, 0xEB));
        }
        if (key.Contains("explorer") || key.Contains("资源管理器"))
        {
            return (Color.FromRgb(0x00, 0x78, 0xD4), Color.FromRgb(0xEB, 0xF3, 0xFC), Color.FromRgb(0xB3, 0xD7, 0xFF));
        }
        if (key.Contains("通用") || string.IsNullOrWhiteSpace(processName))
        {
            return (Color.FromRgb(0x00, 0x67, 0xC0), Color.FromRgb(0xEB, 0xF3, 0xFC), Color.FromRgb(0xB3, 0xD7, 0xFF));
        }

        int hash = Math.Abs(softwareName.GetHashCode());
        Color[] palette =
        [
            Color.FromRgb(0x00, 0x67, 0xC0),
            Color.FromRgb(0x00, 0x82, 0x72),
            Color.FromRgb(0x74, 0x4D, 0xA9),
            Color.FromRgb(0x87, 0x64, 0xB8),
            Color.FromRgb(0x49, 0x82, 0x05),
            Color.FromRgb(0x03, 0x83, 0x87)
        ];
        var c = palette[hash % palette.Length];
        return (c, Color.FromArgb(0x18, c.R, c.G, c.B), Color.FromArgb(0x60, c.R, c.G, c.B));
    }

    private void RefreshPackages()
    {
        _packages = ActionStore.LoadAll();
        foreach (var package in _packages)
        {
            package.SoftwareIcon = string.IsNullOrWhiteSpace(package.ProcessName)
                ? CreateFallbackIcon()
                : GetIconForProcess(package.ProcessName);
        }

        // 按软件分类归组
        var groups = _packages.GroupBy(p => p.SoftwareName, StringComparer.OrdinalIgnoreCase);
        var categories = new List<SoftwareCategory>();

        foreach (var group in groups)
        {
            var first = group.First();
            var (primary, lightBg, border) = GetSoftwareThemeColors(first.SoftwareName, first.ProcessName);

            var primaryBrush = new SolidColorBrush(primary);
            primaryBrush.Freeze();
            var lightBrush = new SolidColorBrush(lightBg);
            lightBrush.Freeze();
            var borderBrush = new SolidColorBrush(border);
            borderBrush.Freeze();

            var category = new SoftwareCategory
            {
                Name = first.SoftwareName,
                Icon = first.SoftwareIcon as ImageSource,
                ProcessName = first.ProcessName,
                PrimaryBrush = primaryBrush,
                LightBackgroundBrush = lightBrush,
                BorderBrush = borderBrush
            };

            foreach (var pkg in group.OrderBy(p => p.Name, StringComparer.CurrentCulture))
            {
                pkg.ThemePrimaryBrush = primaryBrush;
                pkg.ThemeLightBrush = lightBrush;
                pkg.ThemeBorderBrush = borderBrush;
                category.Packages.Add(pkg);
            }

            categories.Add(category);
        }

        // 全局通用排最前面置顶，其余按字母排序
        _categories = categories
            .OrderBy(c => IsGeneralSoftware(c.Name) ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();

        string? previousCategoryName = _selectedCategory?.Name;
        SoftwareCategoryList.ItemsSource = _categories;

        int targetIndex = 0;
        if (!string.IsNullOrEmpty(previousCategoryName))
        {
            int idx = _categories.FindIndex(c => string.Equals(c.Name, previousCategoryName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                targetIndex = idx;
            }
        }

        if (_categories.Count > 0 && _isCategoryExpanded)
        {
            SoftwareCategoryList.SelectedIndex = targetIndex;
            ExpandCategory(_categories[targetIndex]);
        }
        else
        {
            CollapseCategory();
        }

        RefreshPackageBox();
    }

    private void PackageArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;

        // 软件卡片与指令卡片共享响应式列数，卡片平分宽度随窗口自适应伸缩
        // 恢复软件卡片的大气基准宽度（约 215 像素，标准窗口下整齐呈现 4 列）
        double availableWidth = e.NewSize.Width - 36;
        int cols = (int)(availableWidth / 215.0);
        if (cols < 2) cols = 2;
        if (cols > 6) cols = 6;

        if (GridColumns != cols)
        {
            GridColumns = cols;
        }
    }

    private void PackageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(dep);
            if (scrollViewer != null)
            {
                // 确保在任何卡片或内容区域上均可顺畅响应滚轮，平滑滚动
                double delta = e.Delta > 0 ? -48.0 : 48.0;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + delta);
                e.Handled = true;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void SoftwareCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SoftwareCategory clickedCategory)
        {
            if (_isCategoryExpanded && _selectedCategory == clickedCategory)
            {
                // 再次点击同一个软件卡片 -> 优雅收起下方指令集！
                CollapseCategory();
                e.Handled = true;
                return;
            }

            // 点击不同软件或当前处于收起状态 -> 展开该软件的指令集
            ExpandCategory(clickedCategory);
            e.Handled = true;
        }
    }

    private void SoftwareCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoftwareCategoryList.SelectedItem is SoftwareCategory category)
        {
            if (!_isCategoryExpanded || _selectedCategory != category)
            {
                ExpandCategory(category);
            }
        }
    }

    private void ExpandCategory(SoftwareCategory category)
    {
        _isCategoryExpanded = true;
        _selectedCategory = category;
        SoftwareCategoryList.SelectedItem = category;

        // 展现下方指令集面板
        SoftwarePackagePanel.Visibility = Visibility.Visible;

        // 更新下方展开面板的专属横幅、色条与文字色彩
        BannerColorBar.Background = category.PrimaryBrush;
        BannerSoftwareIcon.Source = category.Icon;
        BannerSoftwareTitle.Text = $"{category.Name} 专属指令集";
        BannerSoftwareTitle.Foreground = category.PrimaryBrush;

        bool isGeneral = IsGeneralSoftware(category.Name);
        BannerSoftwareSubtitle.Text = isGeneral
            ? "本组指令为全局基础通用指令，在所有软件中均可呼出使用；再次点击卡片或右侧按钮可收起"
            : $"本组指令专为【{category.Name}】定制生效；再次点击卡片或右侧按钮可收起";

        BannerBadgeBorder.BorderBrush = category.BorderBrush;
        BannerBadgeText.Text = $"{category.ItemCount} 个指令";
        BannerBadgeText.Foreground = category.PrimaryBrush;

        SelectedSoftwareBanner.Background = category.LightBackgroundBrush;
        SelectedSoftwareBanner.BorderBrush = category.BorderBrush;

        // 绑定下方指令卡片流（UniformGrid 共享 GridColumns，数量与宽度与上方软件对齐）
        PackageList.ItemsSource = category.Packages;

        // 重置说明栏
        PackageList.SelectedIndex = -1;
        PackageDetailTitle.Text = $"{category.Name} 说明";
        PackageDetailSoftware.Text = $"所属分类：{category.Name}";
        PackageDescText.Text = $"当前已展开【{category.Name}】分类（共 {category.ItemCount} 个指令）。点击具体指令卡片可在此查看详细功能说明，支持 Ctrl/Shift 多选；再次点击上方卡片可收起。";
    }

    private void CollapseCategory()
    {
        _isCategoryExpanded = false;
        _selectedCategory = null;
        SoftwareCategoryList.SelectedIndex = -1;
        SoftwarePackagePanel.Visibility = Visibility.Collapsed;

        // 重置说明栏
        PackageDetailTitle.Text = "指令包说明";
        PackageDetailSoftware.Text = "所属分类：未选择（已收起）";
        PackageDescText.Text = "已收起指令集。点击上方任意软件卡片可展开其专属指令集；可将 .zip 压缩包拖入快速安装，支持 Ctrl/Shift 多选。";
    }

    private void CollapseCategory_Click(object sender, RoutedEventArgs e)
    {
        CollapseCategory();
    }

    private static bool IsGeneralSoftware(string? name)
    {
        return name is "全局通用" or "通用" or "系统通用";
    }

    private void RefreshPackageBox()
    {
        string? currentProcess = null;
        if (MainTabs.SelectedIndex == 1 && _sceneIndex >= 0 && _sceneIndex < _config.Apps.Count)
        {
            currentProcess = _config.Apps[_sceneIndex].ProcessName;
        }

        IEnumerable<ActionPackage> filtered;
        if (MainTabs.SelectedIndex == 1 && _sceneIndex < 0)
        {
            filtered = _packages.Where(p => string.IsNullOrEmpty(p.ProcessName));
        }
        else if (MainTabs.SelectedIndex == 1 && currentProcess is not null)
        {
            filtered = _packages.Where(p =>
                string.Equals(p.ProcessName, currentProcess, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            filtered = _packages;
        }

        PackageBox.ItemsSource = filtered.ToList();
    }

    private void RefreshPackages_Click(object sender, RoutedEventArgs e)
    {
        RefreshPackages();
    }

    private void InstallPackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择指令包（支持多选）",
            Filter = "指令包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            int successCount = 0;
            var errors = new List<string>();
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    ActionStore.Install(file);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{System.IO.Path.GetFileName(file)}: {ex.Message}");
                }
            }

            RefreshPackages();

            if (errors.Count == 0)
            {
                MessageBox.Show(
                    dialog.FileNames.Length == 1 ? "指令包已安装成功。" : $"已成功批量安装 {successCount} 个指令包。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"已成功安装 {successCount} 个，失败 {errors.Count} 个：\n" + string.Join("\n", errors.Take(5)),
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void UninstallPackage_Click(object sender, RoutedEventArgs e)
    {
        var selected = PackageList.SelectedItems.OfType<ActionPackage>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先在列表里选中一个或多个要卸载的指令包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string confirmMsg = selected.Count == 1
            ? $"确定要卸载指令包「{selected[0].Name}」吗？\n（已配置该指令的软件分格将自动清空）"
            : $"确定要批量卸载选中的 {selected.Count} 个指令包吗？\n（{string.Join("、", selected.Take(3).Select(p => p.Name))}{(selected.Count > 3 ? " 等" : "")}，已配置的相关分格将自动清空）";

        var confirm = MessageBox.Show(confirmMsg, "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        int uninstalledCount = 0;
        int cleanedSlotsCount = 0;

        foreach (var package in selected)
        {
            try
            {
                ActionStore.Uninstall(package);
                uninstalledCount++;
                cleanedSlotsCount += CleanReferencesToPackage(package);
            }
            catch
            {
                // 忽略单个文件被占用的非致命异常
            }
        }

        if (cleanedSlotsCount > 0)
        {
            ConfigStore.Save(_config);
            ReloadCurrentSlots();
        }

        RefreshPackages();
        RefreshPackageBox();

        string summary = uninstalledCount == 1
            ? $"指令包「{selected[0].Name}」已卸载成功"
            : $"已成功批量卸载 {uninstalledCount} 个指令包";

        if (cleanedSlotsCount > 0)
        {
            summary += $"，并自动清理了 {cleanedSlotsCount} 处相关软件分格";
        }
        summary += "。";

        NotificationService.ShowSuccess("指令包卸载", summary);
        MessageBox.Show(summary, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private int CleanReferencesToPackage(ActionPackage package)
    {
        int count = 0;
        string pkgDir = package.DirectoryPath.TrimEnd('\\', '/');
        string pkgFolderName = System.IO.Path.GetFileName(pkgDir);

        bool MatchesAction(RadialAction a, string currentProcessName)
        {
            if (a.IsBlank) return false;

            // 1. 脚本路径匹配（最强匹配）
            if (a.Type == ActionType.RunScript && !string.IsNullOrWhiteSpace(a.Target))
            {
                string target = a.Target.TrimEnd('\\', '/');
                if (target.StartsWith(pkgDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(pkgFolderName) &&
                    (target.Contains($"\\actions\\{pkgFolderName}\\", StringComparison.OrdinalIgnoreCase) ||
                     target.Contains($"/actions/{pkgFolderName}/", StringComparison.OrdinalIgnoreCase) ||
                     target.Contains($"\\{pkgFolderName}\\", StringComparison.OrdinalIgnoreCase) ||
                     target.Contains($"/{pkgFolderName}/", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            // 2. 软件专属指令包按进程名与指令名匹配（覆盖快捷键包、打开程序包等所有非脚本类型）
            if (!string.IsNullOrWhiteSpace(package.ProcessName) &&
                string.Equals(currentProcessName, package.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Name, package.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. 全局或无进程指令包按名字、类型、目标一致匹配
            if (string.Equals(a.Name, package.Name, StringComparison.OrdinalIgnoreCase) &&
                a.Type == package.Type &&
                string.Equals(a.Target, package.Target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        // 1. 清理全局动作
        for (int i = 0; i < _config.GlobalActions.Count; i++)
        {
            if (MatchesAction(_config.GlobalActions[i], ""))
            {
                _config.GlobalActions[i] = RadialAction.Empty;
                count++;
            }
        }
        TrimTrailingBlanks(_config.GlobalActions);

        // 2. 清理所有分软件动作
        foreach (var app in _config.Apps)
        {
            for (int i = 0; i < app.Actions.Count; i++)
            {
                if (MatchesAction(app.Actions[i], app.ProcessName))
                {
                    app.Actions[i] = RadialAction.Empty;
                    count++;
                }
            }
            TrimTrailingBlanks(app.Actions);
        }

        return count;
    }

    private void AutoCleanOrphanActions()
    {
        bool changed = false;

        // 检查全局动作中指向已删除脚本的孤儿
        for (int i = 0; i < _config.GlobalActions.Count; i++)
        {
            var a = _config.GlobalActions[i];
            if (!a.IsBlank && a.Type == ActionType.RunScript && !string.IsNullOrWhiteSpace(a.Target))
            {
                if (a.Target.Contains("actions", StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(a.Target))
                {
                    _config.GlobalActions[i] = RadialAction.Empty;
                    changed = true;
                }
            }
        }
        if (changed) TrimTrailingBlanks(_config.GlobalActions);

        // 检查分软件动作中指向已删除脚本的孤儿
        foreach (var app in _config.Apps)
        {
            bool appChanged = false;
            for (int i = 0; i < app.Actions.Count; i++)
            {
                var a = app.Actions[i];
                if (a.IsBlank) continue;

                if (a.Type == ActionType.RunScript && !string.IsNullOrWhiteSpace(a.Target))
                {
                    if (a.Target.Contains("actions", StringComparison.OrdinalIgnoreCase) && !System.IO.File.Exists(a.Target))
                    {
                        app.Actions[i] = RadialAction.Empty;
                        appChanged = true;
                    }
                }
            }

            if (appChanged)
            {
                TrimTrailingBlanks(app.Actions);
                changed = true;
            }
        }

        if (changed)
        {
            ConfigStore.Save(_config);
        }
    }

    private void PackageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PackageBox.SelectedItem is not ActionPackage package)
        {
            return;
        }

        LoadActionIntoEditor(package.Name, package.Type, package.Target, package.Icon);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var zipFiles = files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (zipFiles.Count == 0)
            {
                NotificationService.ShowWarning("指令包安装", "请拖入 .zip 格式的指令包文件。");
                return;
            }

            int successCount = 0;
            var errors = new List<string>();
            var installedNames = new List<string>();

            foreach (var file in zipFiles)
            {
                try
                {
                    ActionStore.Install(file);
                    successCount++;
                    installedNames.Add(System.IO.Path.GetFileNameWithoutExtension(file));
                }
                catch (Exception ex)
                {
                    errors.Add($"{System.IO.Path.GetFileName(file)}: {ex.Message}");
                }
            }

            RefreshPackages();
            RefreshPackageBox();

            if (errors.Count == 0)
            {
                string msg = successCount == 1
                    ? $"指令包「{installedNames[0]}」已安装成功！"
                    : $"已成功批量安装 {successCount} 个指令包！";

                NotificationService.ShowSuccess("指令包安装", msg);
                MessageBox.Show(msg, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string errDetail = string.Join("\n", errors.Take(5));
                NotificationService.ShowError("指令包安装", $"安装完成：成功 {successCount} 个，失败 {errors.Count} 个。");
                MessageBox.Show(
                    $"已成功安装 {successCount} 个，失败 {errors.Count} 个：\n" + errDetail,
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void LoadGeneralSettings()
    {
        _loading = true;
        try
        {
            TriggerModeBox.ItemsSource = new[] { "中键", "右键", "Ctrl+中键" };
            TriggerModeBox.SelectedIndex = _config.TriggerMode switch
            {
                "Middle" => 0,
                "Right" => 1,
                "CtrlMiddle" => 2,
                _ => 2
            };

            HoldBox.ItemsSource = new[] { "0", "100", "150", "200", "300", "400", "500" };
            HoldBox.Text = _config.HoldMilliseconds.ToString();

            MoveBox.ItemsSource = new[] { "10", "20", "30", "50", "80" };
            MoveBox.Text = _config.MoveThresholdPixels.ToString();

            SizeBox.ItemsSource = new[] { "280", "320", "380", "440", "510", "590", "680", "760" };
            SizeBox.SelectedItem = SizeBox.Items.Contains(_config.RadialSize) ? _config.RadialSize : "380";

            ThemeBox.ItemsSource = new[] { "经典白底 (Fluent)", "亚克力毛玻璃 (Acrylic)", "极简纯白 (White)", "深邃暗黑 (Dark)" };
            ThemeBox.SelectedIndex = _config.Theme switch
            {
                "Glass" => 1,
                "White" => 2,
                "Dark" => 3,
                _ => 0
            };

            AutoStartCheck.IsChecked = _config.AutoStart;
            ErrorBalloonCheck.IsChecked = _config.ShowErrorBalloon;
        }
        finally
        {
            _loading = false;
        }
    }

    private void General_Changed(object sender, RoutedEventArgs e)
    {
        SaveGeneral();
    }

    private void SaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        SaveGeneral();
        MessageBox.Show("设置已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveGeneral()
    {
        if (_loading)
        {
            return;
        }

        _config.TriggerMode = TriggerModeBox.SelectedIndex switch
        {
            0 => "Middle",
            1 => "Right",
            2 => "CtrlMiddle",
            _ => "CtrlMiddle"
        };
        _config.HoldMilliseconds = int.TryParse(HoldBox.Text, out int hold) ? hold : 200;
        _config.MoveThresholdPixels = int.TryParse(MoveBox.Text, out int move) ? move : 30;
        _config.RadialSize = SizeBox.SelectedItem as string ?? "380";
        _config.Theme = ThemeBox.SelectedIndex switch
        {
            1 => "Glass",
            2 => "White",
            3 => "Dark",
            _ => "LightBlue"
        };
        _config.AutoStart = AutoStartCheck.IsChecked == true;
        _config.ShowErrorBalloon = ErrorBalloonCheck.IsChecked == true;

        ConfigStore.Save(_config);
        _onChanged();
    }

    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出配置",
            FileName = $"Pinwheel-config-{DateTime.Now:yyyyMMdd}.json",
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ConfigStore.SaveTo(_config, dialog.FileName);
            MessageBox.Show("配置已导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入配置",
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AppConfig imported;
        try
        {
            imported = ConfigStore.LoadFrom(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var result = MessageBox.Show(
            "导入会覆盖当前全部设置，确定继续吗？",
            "确认导入",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyImportedConfig(imported);
        ConfigStore.Save(_config);
        _onChanged();
        ReloadAll();
        MessageBox.Show("配置已导入。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var backups = ConfigStore.ListBackups();
        if (backups.Count == 0)
        {
            MessageBox.Show("还没有配置备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "恢复配置备份",
            InitialDirectory = ConfigStore.BackupDirectory,
            Filter = "JSON 配置文件 (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = MessageBox.Show(
            "恢复会覆盖当前全部设置，确定继续吗？",
            "确认恢复",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppConfig restored = ConfigStore.LoadFrom(dialog.FileName);
            ApplyImportedConfig(restored);
            ConfigStore.Save(_config);
            _onChanged();
            ReloadAll();
            MessageBox.Show("配置已恢复。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"恢复失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadLog()
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log");
            LogBox.Text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
            LogBox.ScrollToEnd();
        }
        catch (Exception ex)
        {
            LogBox.Text = $"读取日志失败：{ex.Message}";
        }
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e)
    {
        LoadLog();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定清空日志吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log"), "");
            LoadLog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"清空失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log");
            if (!System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, "", System.Text.Encoding.UTF8);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开日志文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "debug.log");
            if (System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppContext.BaseDirectory}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AboutOpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrlOrFile("https://github.com");
    }

    private void AboutOpenChangelog_Click(object sender, RoutedEventArgs e)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!System.IO.File.Exists(path))
        {
            string parentPath = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "CHANGELOG.md");
            if (System.IO.File.Exists(parentPath))
            {
                path = parentPath;
            }
        }

        if (System.IO.File.Exists(path))
        {
            OpenUrlOrFile(path);
        }
        else
        {
            MessageBox.Show("未在程序目录下找到 CHANGELOG.md 文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AboutOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppContext.BaseDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AboutCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v0.1.0";
        var result = MessageBox.Show(
            $"当前已是最新稳定版本：{verStr}\n\n是否打开 GitHub Releases 页面查看更新或下载便携包？",
            "检查更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            OpenUrlOrFile("https://github.com");
        }
    }

    private static void OpenUrlOrFile(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyImportedConfig(AppConfig imported)
    {
        _config.TriggerMode = imported.TriggerMode;
        _config.HoldMilliseconds = imported.HoldMilliseconds;
        _config.MoveThresholdPixels = imported.MoveThresholdPixels;
        _config.RadialSize = imported.RadialSize;
        _config.Theme = imported.Theme;
        _config.AutoStart = imported.AutoStart;
        _config.ShowErrorBalloon = imported.ShowErrorBalloon;
        _config.GlobalActions = imported.GlobalActions;
        _config.Apps = imported.Apps;
    }

    private void ReloadAll()
    {
        _selectedApp = null;
        _sceneIndex = -1;
        _slotIndex = -1;
        LoadGeneralSettings();
        ReloadScenes();
        ReloadCurrentSlots();
        RefreshRightPane();
        UpdateEditorVisibility();
        RefreshPackageBox();
    }

    private void RefreshRunningApps()
    {
        RunningAppsBox.Items.Clear();

        var map = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrEmpty(process.MainWindowTitle))
                {
                    continue;
                }

                if (string.Equals(process.ProcessName, "Pinwheel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = process.ProcessName + ".exe";
                if (map.ContainsKey(name))
                {
                    continue;
                }

                ImageSource? icon = null;
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        icon = GetAssociatedIcon(path);
                    }
                }
                catch
                {
                    // 忽略。
                }

                map[name] = new RunningApp(name, icon ?? CreateFallbackIcon());
            }
            catch
            {
                // 忽略。
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var item in map.Values.OrderBy(a => a.Name))
        {
            RunningAppsBox.Items.Add(item);
        }
    }

    private static ImageSource? GetAssociatedIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? CreateFallbackIcon()
    {
        try
        {
            string localAsset = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "Pinwheel-32.png");
            if (System.IO.File.Exists(localAsset))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage(new Uri(localAsset, UriKind.Absolute));
                bmp.Freeze();
                return bmp;
            }
        }
        catch
        {
            // 忽略。
        }

        try
        {
            var uri = new Uri("pack://application:,,,/assets/Pinwheel-32.png", UriKind.Absolute);
            var bmp = new System.Windows.Media.Imaging.BitmapImage(uri);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            try
            {
                using var icon = System.Drawing.SystemIcons.Application;
                var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                bs.Freeze();
                return bs;
            }
            catch
            {
                return null;
            }
        }
    }

    private static ImageSource? GetIconForProcess(string processName)
    {
        if (ProcessIconCache.TryGetValue(processName, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? result = null;
        try
        {
            string? path = ResolveProcessPath(processName);
            if (!string.IsNullOrEmpty(path))
            {
                result = GetAssociatedIcon(path);
            }
        }
        catch
        {
            // 忽略。
        }

        result ??= CreateFallbackIcon();
        ProcessIconCache[processName] = result;
        return result;
    }

    private static string? ResolveProcessPath(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        string fileName = processName.Trim();
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".exe";
        }
        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

        // 1. 优先从当前运行中的进程取
        var processes = System.Diagnostics.Process.GetProcessesByName(baseName);
        if (processes.Length > 0)
        {
            try
            {
                string? runningPath = processes[0].MainModule?.FileName;
                if (!string.IsNullOrEmpty(runningPath) && System.IO.File.Exists(runningPath))
                {
                    return runningPath;
                }
            }
            catch
            {
                // 忽略。
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        // 2. 检查系统目录（Windows / System32）
        string winPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), fileName);
        if (System.IO.File.Exists(winPath))
        {
            return winPath;
        }
        string sysPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), fileName);
        if (System.IO.File.Exists(sysPath))
        {
            return sysPath;
        }

        // 3. 从注册表 App Paths 中查找（HKLM 和 HKCU，不区分大小写）
        string[] registrySubKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + fileName,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + fileName
        ];

        foreach (string subKey in registrySubKeys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
                if (key?.GetValue(null) is string registeredPath &&
                    !string.IsNullOrEmpty(registeredPath) &&
                    System.IO.File.Exists(registeredPath))
                {
                    return registeredPath;
                }
            }
            catch
            {
                // 忽略。
            }

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey);
                if (key?.GetValue(null) is string registeredPath &&
                    !string.IsNullOrEmpty(registeredPath) &&
                    System.IO.File.Exists(registeredPath))
                {
                    return registeredPath;
                }
            }
            catch
            {
                // 忽略。
            }
        }

        // 4. 常见安装路径快速枚举
        string[] searchRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ];
        foreach (string root in searchRoots)
        {
            if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var files = System.IO.Directory.EnumerateFiles(root, fileName, System.IO.SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    return file;
                }
            }
            catch
            {
                // 忽略无权限目录
            }
        }

        return null;
    }

    private void RefreshApps_Click(object sender, RoutedEventArgs e)
    {
        RefreshRunningApps();
    }

    private void ReloadScenes()
    {
        AppProfile? previousApp = _selectedApp;
        bool previousWasGlobal = _selectedApp is null;

        SceneList.Items.Clear();
        SceneList.Items.Add(new SceneItem("全局设置", null, "🌐", null));

        var orderedApps = _config.Apps
            .Where(app => _searchText.Length == 0 ||
                          app.ProcessName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                          ActionPackage.FormatProcessName(app.ProcessName)
                              .Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(app => ActionPackage.FormatProcessName(app.ProcessName), StringComparer.CurrentCulture);

        foreach (var app in orderedApps)
        {
            string softwareName = ActionPackage.FormatProcessName(app.ProcessName);
            string label = app.Enabled ? $"{softwareName}（启用）" : $"{softwareName}（禁用）";
            SceneList.Items.Add(new SceneItem(label, GetIconForProcess(app.ProcessName), "", app));
        }

        int target = 0;
        if (previousApp is not null)
        {
            for (int i = 1; i < SceneList.Items.Count; i++)
            {
                if (SceneList.Items[i] is SceneItem { Profile: AppProfile profile } &&
                    ReferenceEquals(profile, previousApp))
                {
                    target = i;
                    break;
                }
            }
        }
        else if (!previousWasGlobal && SceneList.Items.Count > 1)
        {
            target = 1;
        }

        SceneList.SelectedIndex = target;
    }

    private void SceneSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SceneSearchBox.Text.Trim();
        SceneSearchHint.Visibility = _searchText.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReloadScenes();
    }

    private List<RadialAction> GetCurrentActions()
    {
        if (MainTabs.SelectedIndex == 1)
        {
            if (_sceneIndex < 0)
            {
                return _config.GlobalActions;
            }
            if (_sceneIndex < _config.Apps.Count)
            {
                return _config.Apps[_sceneIndex].Actions;
            }
        }
        return new List<RadialAction>();
    }

    private void ReloadCurrentSlots()
    {
        if (MainTabs.SelectedIndex != 1)
        {
            return;
        }

        if (_sceneIndex < 0)
        {
            ReloadGlobalSlots();
        }
        else
        {
            ReloadAppSlots();
        }
    }

    private void RefreshRightPane()
    {
        bool isGlobal = MainTabs.SelectedIndex == 1 && _sceneIndex < 0;
        GlobalScroll.Visibility = isGlobal ? Visibility.Visible : Visibility.Collapsed;
        AppScroll.Visibility = isGlobal ? Visibility.Collapsed : Visibility.Visible;
        AppControlsRow.Visibility = isGlobal ? Visibility.Collapsed : Visibility.Visible;
        LayoutRow.Visibility = isGlobal ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateEditorVisibility()
    {
        EditorPanel.Visibility = MainTabs.SelectedIndex == 1 && _slotIndex >= 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        _slotIndex = -1;
        UpdateEditorVisibility();
        ReloadCurrentSlots();
    }

    private void AppSlotPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Path { Tag: int index })
        {
            _dragFromIndex = index;
            _dragStartPoint = e.GetPosition(AppSlotPanel);
            _dragging = false;
            return;
        }

        _dragFromIndex = -1;
        _dragging = false;

        if (e.OriginalSource is not Path)
        {
            _slotIndex = -1;
            UpdateEditorVisibility();
            ReloadAppSlots();
        }
    }

    private void AppSlotPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFromIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(AppSlotPanel);
        if (!_dragging &&
            (Math.Abs(pos.X - _dragStartPoint.X) > 8 || Math.Abs(pos.Y - _dragStartPoint.Y) > 8))
        {
            _dragging = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
    }

    private void AppSlotPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        int from = _dragFromIndex;
        bool wasDragging = _dragging;
        int targetIndex = GetSliceIndex(e.OriginalSource);

        _dragFromIndex = -1;
        _dragging = false;
        Mouse.OverrideCursor = null;

        if (from < 0)
        {
            return;
        }

        if (wasDragging && targetIndex >= 0 && targetIndex != from)
        {
            SwapAppActions(from, targetIndex);
        }
        else if (!wasDragging && targetIndex == from)
        {
            SelectAppSlot(from);
        }
    }

    private int GetSliceIndex(object source)
    {
        return source is Path { Tag: int index } ? index : -1;
    }

    private void SelectAppSlot(int index)
    {
        _slotIndex = index;
        var actions = GetCurrentActions();
        if (index < actions.Count && !actions[index].IsBlank)
        {
            var action = actions[index];
            LoadActionIntoEditor(action.Name, action.Type, action.Target, action.Icon, action.Steps, action.DelayMs);
        }
        else
        {
            LoadActionIntoEditor("", ActionType.OpenProgram, "", "");
        }

        ReloadAppSlots();
        UpdateEditorVisibility();
    }

    private void SwapAppActions(int from, int to)
    {
        if (_sceneIndex < 0 || _sceneIndex >= _config.Apps.Count)
        {
            return;
        }

        var actions = _config.Apps[_sceneIndex].Actions;
        int max = Math.Max(from, to);
        while (actions.Count <= max)
        {
            actions.Add(RadialAction.Empty);
        }

        (actions[from], actions[to]) = (actions[to], actions[from]);
        TrimTrailingBlanks(actions);
        ConfigStore.Save(_config);
        _slotIndex = to;
        ReloadAppSlots();
    }

    private void GlobalSlotPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == GlobalSlotPanel)
        {
            _slotIndex = -1;
            UpdateEditorVisibility();
            ReloadGlobalSlots();
        }
    }

    private void LoadActionIntoEditor(
        string name,
        ActionType type,
        string target,
        string icon,
        List<RadialAction>? steps = null,
        int delayMs = 0)
    {
        _loadingEditor = true;
        try
        {
            NameBox.Text = name;
            IconBox.Text = icon;
            TypeBox.SelectedIndex = (int)type;
            TargetBox.Text = target;
            _editingSteps = steps?.ToList() ?? new List<RadialAction>();
            _editingDelayMs = delayMs;
        }
        finally
        {
            _loadingEditor = false;
        }

        RefreshStepList();
        UpdateHint();
        UpdateBrowseVisibility();
        UpdateTypeVisibility();
    }

    private void RefreshStepList()
    {
        _loadingSteps = true;
        try
        {
            int previous = StepList.SelectedIndex;
            StepList.ItemsSource = _editingSteps;
            DelayBox.Text = _editingDelayMs.ToString();
            if (_editingSteps.Count > 0)
            {
                StepList.SelectedIndex = previous >= 0 && previous < _editingSteps.Count ? previous : 0;
            }
            else
            {
                StepNameBox.Text = "";
                StepTypeBox.SelectedIndex = 0;
                StepTargetBox.Text = "";
                StepIconBox.Text = "";
            }
        }
        finally
        {
            _loadingSteps = false;
        }
    }

    private void ReloadGlobalSlots()
    {
        GlobalSlotPanel.Children.Clear();
        var actions = _config.GlobalActions;
        int count = Math.Max(8, actions.Count);
        for (int i = 0; i < count; i++)
        {
            var action = i < actions.Count ? actions[i] : null;
            var button = CreateSlotButton(action, i, 145, 66);
            button.Margin = new Thickness(0, 0, 10, 10);
            GlobalSlotPanel.Children.Add(button);
        }
    }

    private void ReloadAppSlots()
    {
        AppSlotPanel.Children.Clear();
        if (_sceneIndex < 0 || _sceneIndex >= _config.Apps.Count)
        {
            return;
        }

        var actions = _config.Apps[_sceneIndex].Actions;
        bool twoRings = SlotLayoutBox.SelectedIndex != 1;
        double cx = 260, cy = 190;

        if (twoRings)
        {
            AddRing(AppSlotPanel, cx, cy, 55, 105);
            AddRing(AppSlotPanel, cx, cy, 115, 175);

            for (int i = 0; i < 8; i++)
            {
                var action = i < actions.Count ? actions[i] : null;
                if (action != null && action.IsBlank) action = null;
                AddSlice(AppSlotPanel, cx, cy, -90 + i * 45, 45 - 4, 55, 105, action, i, true, false);
            }

            for (int i = 0; i < 16; i++)
            {
                double mid = -90 + i * 22.5;
                if (i < 8)
                {
                    int index = 8 + i;
                    var action = index < actions.Count ? actions[index] : null;
                    if (action != null && action.IsBlank) action = null;
                    AddSlice(AppSlotPanel, cx, cy, mid, 22.5 - 2, 115, 175, action, index, true, false);
                }
                else
                {
                    int globalIndex = 15 - i;
                    var globalAction = globalIndex < _config.GlobalActions.Count ? _config.GlobalActions[globalIndex] : null;
                    if (globalAction != null && globalAction.IsBlank) globalAction = null;
                    AddSlice(AppSlotPanel, cx, cy, mid, 22.5 - 2, 115, 175, globalAction, globalIndex, false, true);
                }
            }
        }
        else
        {
            AddRing(AppSlotPanel, cx, cy, 55, 105);
            for (int i = 0; i < 8; i++)
            {
                var action = i < actions.Count ? actions[i] : null;
                if (action != null && action.IsBlank) action = null;
                AddSlice(AppSlotPanel, cx, cy, -90 + i * 45, 45 - 4, 55, 105, action, i, true, false);
            }
        }
    }

    private void SlotLayoutBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        ReloadAppSlots();
    }

    private void PackageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = PackageList.SelectedItems.OfType<ActionPackage>().ToList();
        if (selected.Count == 1)
        {
            var pkg = selected[0];
            PackageDetailTitle.Text = $"「{pkg.Name}」详细说明";
            PackageDetailSoftware.Text = string.IsNullOrWhiteSpace(pkg.SoftwareName) ? "" : $"所属分类：{pkg.SoftwareName}";
            PackageDescText.Text = string.IsNullOrWhiteSpace(pkg.Description) ? "该指令包暂无详细描述说明。" : pkg.Description;
        }
        else if (selected.Count > 1)
        {
            PackageDetailTitle.Text = $"已批量选中 {selected.Count} 个指令包";
            PackageDetailSoftware.Text = "";
            PackageDescText.Text = $"当前已批量选中 {selected.Count} 个指令包（{string.Join("、", selected.Select(p => p.Name))}）。您可以点击上方「卸载选中」进行批量卸载。";
        }
        else
        {
            PackageDetailTitle.Text = "指令包说明";
            PackageDetailSoftware.Text = "";
            PackageDescText.Text = "在上方列表中点击或按住 Ctrl/Shift 多选指令包，可在此处查看详细功能说明；安装和卸载均已支持多选。";
        }
    }

    private Button CreateSlotButton(RadialAction? action, int index, double width, double height)
    {
        bool isBlank = action == null || action.IsBlank;
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isBlank)
        {
            content.Children.Add(new TextBlock
            {
                Text = "➕",
                FontSize = 18,
                Opacity = 0.45,
                FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
                TextAlignment = TextAlignment.Center,
                LineHeight = 24
            });
            content.Children.Add(new TextBlock
            {
                Text = $"第 {index + 1} 格 (空白)",
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = action!.Icon,
                FontSize = 22,
                FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
                TextAlignment = TextAlignment.Center,
                LineHeight = 26
            });
            content.Children.Add(new TextBlock
            {
                Text = action.Name,
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif"),
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Tag = index,
            Padding = new Thickness(6, 4, 6, 4),
            Background = _slotIndex == index
                ? new SolidColorBrush(Color.FromRgb(0xDE, 0xEC, 0xF9))
                : (isBlank ? new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xFA)) : Brushes.White),
            BorderBrush = _slotIndex == index
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0))
                : new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            BorderThickness = new Thickness(_slotIndex == index ? 2 : 1)
        };
        button.Click += Slot_Click;
        return button;
    }

    private static void AddRing(Canvas canvas, double centerX, double centerY, double innerRadius, double outerRadius)
    {
        var stroke = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        stroke.Freeze();

        var outer = new Ellipse
        {
            Width = outerRadius * 2,
            Height = outerRadius * 2,
            Stroke = stroke,
            StrokeThickness = 1.2
        };
        Canvas.SetLeft(outer, centerX - outerRadius);
        Canvas.SetTop(outer, centerY - outerRadius);
        canvas.Children.Add(outer);

        var inner = new Ellipse
        {
            Width = innerRadius * 2,
            Height = innerRadius * 2,
            Stroke = stroke,
            StrokeThickness = 1.2
        };
        Canvas.SetLeft(inner, centerX - innerRadius);
        Canvas.SetTop(inner, centerY - innerRadius);
        canvas.Children.Add(inner);
    }

    private void AddSlice(Canvas canvas, double cx, double cy, double midDeg, double span, double innerR, double outerR, RadialAction? action, int index, bool selectable, bool isGlobal)
    {
        var path = new Path
        {
            Data = CreateSliceGeometry(cx, cy, midDeg, span, innerR, outerR),
            Fill = isGlobal
                ? new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8))
                : (selectable && _slotIndex == index
                    ? new SolidColorBrush(Color.FromRgb(0xDE, 0xEC, 0xF9))
                    : Brushes.White),
            Stroke = selectable && _slotIndex == index
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0))
                : new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            StrokeThickness = selectable && _slotIndex == index ? 2 : 1.2
        };
        if (selectable)
        {
            path.Tag = index;
            path.Cursor = Cursors.Hand;
        }
        canvas.Children.Add(path);

        if (action is null || action.IsBlank)
        {
            return;
        }

        var labelBrush = isGlobal
            ? new SolidColorBrush(Color.FromRgb(0x5E, 0x5E, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        double midRadius = (innerR + outerR) / 2;
        var pos = PointOnCircle(cx, cy, midRadius, midDeg);
        double iconSize = outerR > 140 ? 13 : 16;
        double fontSize = outerR > 140 ? 9 : 10;
        double panelWidth = outerR > 140 ? 58 : 64;

        var panel = new StackPanel { Width = panelWidth, IsHitTestVisible = false };
        panel.Children.Add(new TextBlock
        {
            Text = action.Icon,
            FontSize = iconSize,
            FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
            Foreground = labelBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        });
        panel.Children.Add(new TextBlock
        {
            Text = action.Name,
            FontSize = fontSize,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif"),
            FontWeight = FontWeights.Medium,
            Foreground = labelBrush,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Canvas.SetLeft(panel, pos.X - panelWidth / 2);
        Canvas.SetTop(panel, pos.Y - 16);
        canvas.Children.Add(panel);
    }

    private static Geometry CreateSliceGeometry(double cx, double cy, double midDeg, double span, double innerR, double outerR)
    {
        double start = midDeg - span / 2;
        double end = midDeg + span / 2;
        var outerStart = PointOnCircle(cx, cy, outerR, start);
        var outerEnd = PointOnCircle(cx, cy, outerR, end);
        var innerEnd = PointOnCircle(cx, cy, innerR, end);
        var innerStart = PointOnCircle(cx, cy, innerR, start);

        var figure = new PathFigure { StartPoint = outerStart, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerR, outerR), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerR, innerR), 0, false, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double cx, double cy, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _slotIndex = -1;
        UpdateEditorVisibility();
        ReloadCurrentSlots();
        RefreshRightPane();
        RefreshPackageBox();
        if (MainTabs.SelectedIndex == 3)
        {
            LoadLog();
        }
    }

    private void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SceneList.SelectedIndex < 0)
        {
            return;
        }

        _loading = true;
        try
        {
            _selectedApp = (SceneList.SelectedItem as SceneItem)?.Profile;
            _sceneIndex = _selectedApp is null ? -1 : _config.Apps.IndexOf(_selectedApp);
            _slotIndex = -1;
            UpdateEditorVisibility();

            bool isApp = _sceneIndex >= 0;
            if (isApp)
            {
                var app = _config.Apps[_sceneIndex];
                AppEnabledCheck.Visibility = Visibility.Visible;
                AppEnabledCheck.IsChecked = app.Enabled;
                AppFullscreenCheck.Visibility = Visibility.Visible;
                AppFullscreenCheck.IsChecked = app.DisableOnFullscreen ?? true;
                AppTriggerModeBox.Visibility = Visibility.Visible;
                AppTriggerModeBox.SelectedIndex = app.TriggerMode switch
                {
                    "Middle" => 1,
                    "Right" => 2,
                    "CtrlMiddle" => 3,
                    _ => 0
                };
            }
            else
            {
                AppEnabledCheck.Visibility = Visibility.Collapsed;
                AppFullscreenCheck.Visibility = Visibility.Collapsed;
                AppTriggerModeBox.Visibility = Visibility.Collapsed;
            }

            ReloadCurrentSlots();
            RefreshRightPane();
            RefreshPackageBox();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Slot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int index })
        {
            _slotIndex = index;
            var actions = GetCurrentActions();
            if (index < actions.Count && !actions[index].IsBlank)
            {
                var action = actions[index];
                LoadActionIntoEditor(action.Name, action.Type, action.Target, action.Icon, action.Steps, action.DelayMs);
            }
            else
            {
                LoadActionIntoEditor("", ActionType.OpenProgram, "", "");
            }
            ReloadCurrentSlots();
            UpdateEditorVisibility();
        }
    }

    private void SaveSlot_Click(object sender, RoutedEventArgs e)
    {
        var actions = GetCurrentActions();
        if (_slotIndex < 0)
        {
            MessageBox.Show("请先在上方选择一个格子。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        while (actions.Count <= _slotIndex)
        {
            actions.Add(RadialAction.Empty);
        }

        var type = (ActionType)TypeBox.SelectedIndex;
        var action = new RadialAction(
            NameBox.Text.Trim(),
            type,
            TargetBox.Text.Trim(),
            IconBox.Text.Trim());
        if (type == ActionType.Composite)
        {
            action = action with
            {
                Steps = _editingSteps.ToList(),
                DelayMs = int.TryParse(DelayBox.Text, out int delay) ? Math.Max(0, delay) : 250
            };
        }

        actions[_slotIndex] = action;
        TrimTrailingBlanks(actions);

        ConfigStore.Save(_config);
        ReloadCurrentSlots();
        NotificationService.ShowSuccess("Pinwheel", $"已保存第 {_slotIndex + 1} 格的指令");
    }

    private void ClearSlot_Click(object sender, RoutedEventArgs e)
    {
        ClearCurrentSlot();
    }

    private void ClearCurrentSlot()
    {
        if (_slotIndex < 0)
        {
            return;
        }

        var actions = GetCurrentActions();
        if (_slotIndex < actions.Count)
        {
            actions[_slotIndex] = RadialAction.Empty;
            TrimTrailingBlanks(actions);
            ConfigStore.Save(_config);
            LoadActionIntoEditor("", ActionType.OpenProgram, "", "");
            ReloadCurrentSlots();
            NotificationService.ShowSuccess("Pinwheel", $"已清空第 {_slotIndex + 1} 格的指令");
        }
    }

    private static void TrimTrailingBlanks(List<RadialAction> actions)
    {
        for (int i = actions.Count - 1; i >= 0; i--)
        {
            if (actions[i].IsBlank)
            {
                actions.RemoveAt(i);
            }
            else
            {
                break;
            }
        }
    }

    private async void TestAction_Click(object sender, RoutedEventArgs e)
    {
        if (TypeBox.SelectedIndex < 0)
        {
            MessageBox.Show("请先选择指令类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = (ActionType)TypeBox.SelectedIndex;
        if (type is ActionType.SendHotkey or ActionType.SendText)
        {
            var confirm = MessageBox.Show(
                "快捷键和文本会发送到当前前台窗口（现在是设置窗口），建议到目标软件里再测试。\n\n仍然现在测试吗？",
                "提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            name = "测试指令";
        }

        var action = new RadialAction(
            name,
            type,
            TargetBox.Text.Trim(),
            IconBox.Text.Trim());
        if (type == ActionType.Composite)
        {
            action = action with
            {
                Steps = _editingSteps.ToList(),
                DelayMs = int.TryParse(DelayBox.Text, out int delay) ? Math.Max(0, delay) : 250
            };
        }

        bool ok = await ActionRunner.RunAsync(action);
        MessageBox.Show(
            ok ? "测试执行完成。" : "测试执行失败，详情请看右下角提示。",
            "提示",
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private void ExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (TypeBox.SelectedIndex < 0)
        {
            MessageBox.Show("请先选择指令类型。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = (ActionType)TypeBox.SelectedIndex;
        if (type is ActionType.Composite)
        {
            MessageBox.Show("组合指令暂不支持直接导出为单文件指令包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("请先填写名字。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出指令包",
            FileName = $"{name}.zip",
            Filter = "指令包 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string process = _sceneIndex >= 0 && _sceneIndex < _config.Apps.Count
            ? _config.Apps[_sceneIndex].ProcessName
            : "";

        try
        {
            string target = TargetBox.Text.Trim();
            string manifestTarget = target;

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pinwheel-pkg-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempDir);
            try
            {
                if (type == ActionType.RunScript)
                {
                    if (!System.IO.File.Exists(target))
                    {
                        MessageBox.Show("脚本文件不存在，无法导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    string fileName = System.IO.Path.GetFileName(target);
                    System.IO.File.Copy(target, System.IO.Path.Combine(tempDir, fileName), true);
                    manifestTarget = fileName;
                }

                var manifest = new ActionPackageManifest
                {
                    Name = name,
                    Icon = IconBox.Text.Trim(),
                    Type = (int)type,
                    Target = manifestTarget,
                    Process = process,
                    Description = ""
                };

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(tempDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                if (System.IO.File.Exists(dialog.FileName))
                {
                    System.IO.File.Delete(dialog.FileName);
                }

                ZipFile.CreateFromDirectory(tempDir, dialog.FileName);
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.Delete(tempDir, true);
                }
            }

            MessageBox.Show("指令包已导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSteps)
        {
            return;
        }

        if (StepList.SelectedItem is RadialAction step)
        {
            StepNameBox.Text = step.Name;
            StepTypeBox.SelectedIndex = (int)step.Type;
            StepTargetBox.Text = step.Target;
            StepIconBox.Text = step.Icon;
        }
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        var step = new RadialAction(
            string.IsNullOrWhiteSpace(StepNameBox.Text) ? $"步骤 {_editingSteps.Count + 1}" : StepNameBox.Text.Trim(),
            (ActionType)Math.Max(0, StepTypeBox.SelectedIndex),
            StepTargetBox.Text.Trim(),
            string.IsNullOrWhiteSpace(StepIconBox.Text) ? "▶" : StepIconBox.Text.Trim());

        _editingSteps.Add(step);
        RefreshStepList();
        StepList.SelectedIndex = _editingSteps.Count - 1;
    }

    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        int index = StepList.SelectedIndex;
        if (index < 0 || index >= _editingSteps.Count)
        {
            return;
        }

        _editingSteps.RemoveAt(index);
        RefreshStepList();
    }

    private void MoveStepUp_Click(object sender, RoutedEventArgs e)
    {
        int index = StepList.SelectedIndex;
        if (index <= 0 || index >= _editingSteps.Count)
        {
            return;
        }

        (_editingSteps[index - 1], _editingSteps[index]) = (_editingSteps[index], _editingSteps[index - 1]);
        RefreshStepList();
        StepList.SelectedIndex = index - 1;
    }

    private void MoveStepDown_Click(object sender, RoutedEventArgs e)
    {
        int index = StepList.SelectedIndex;
        if (index < 0 || index >= _editingSteps.Count - 1)
        {
            return;
        }

        (_editingSteps[index + 1], _editingSteps[index]) = (_editingSteps[index], _editingSteps[index + 1]);
        RefreshStepList();
        StepList.SelectedIndex = index + 1;
    }

    private void SaveStep_Click(object sender, RoutedEventArgs e)
    {
        int index = StepList.SelectedIndex;
        if (index < 0 || index >= _editingSteps.Count)
        {
            MessageBox.Show("请先在步骤列表里选中一个步骤。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var step = new RadialAction(
            string.IsNullOrWhiteSpace(StepNameBox.Text) ? $"步骤 {index + 1}" : StepNameBox.Text.Trim(),
            (ActionType)Math.Max(0, StepTypeBox.SelectedIndex),
            StepTargetBox.Text.Trim(),
            string.IsNullOrWhiteSpace(StepIconBox.Text) ? "▶" : StepIconBox.Text.Trim());

        _editingSteps[index] = step;
        RefreshStepList();
        StepList.SelectedIndex = index;
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHint();
        UpdateBrowseVisibility();
        UpdateTypeVisibility();
        if (!_loadingEditor)
        {
            MaybeAutoFillNameAndIcon();
        }
    }

    private void UpdateHint()
    {
        HintText.Text = TypeBox.SelectedIndex switch
        {
            0 => "填程序名或完整路径，也可以点「浏览…」选择。",
            1 => "填网址，例如 https://www.baidu.com",
            2 => "填文件夹路径，也可以点「浏览…」选择。",
            3 => "填按键组合，例如 ^c 表示 Ctrl+C，{ENTER} 表示回车",
            4 => "填要发送出去的文本",
            5 => "填脚本文件名（在指令包里）或脚本的完整路径",
            6 => "组合指令：按顺序执行多个步骤，每个步骤都支持脚本等指令类型。",
            _ => string.Empty
        };
    }

    private void UpdateBrowseVisibility()
    {
        BrowseButton.Visibility = TypeBox.SelectedIndex is 0 or 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTypeVisibility()
    {
        bool isComposite = TypeBox.SelectedIndex == (int)ActionType.Composite;
        CompositePanel.Visibility = isComposite ? Visibility.Visible : Visibility.Collapsed;
        TargetField.Visibility = isComposite ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MaybeAutoFillNameAndIcon()
    {
        if (TypeBox.SelectedIndex < 0)
        {
            return;
        }

        var type = (ActionType)TypeBox.SelectedIndex;
        string name = NameBox.Text.Trim();
        bool isBlank = name.Length == 0 || name.StartsWith("格子 ");
        if (isBlank)
        {
            var (defaultName, defaultIcon) = GetTypeDefaults(type);
            NameBox.Text = defaultName;
            IconBox.Text = defaultIcon;
        }
    }

    private static (string Name, string Icon) GetTypeDefaults(ActionType type)
    {
        return type switch
        {
            ActionType.OpenProgram => ("打开程序", "🖥"),
            ActionType.OpenUrl => ("打开网址", "🌐"),
            ActionType.OpenFolder => ("打开文件夹", "📂"),
            ActionType.SendHotkey => ("快捷键", "⌨️"),
            ActionType.SendText => ("发送文本", "📝"),
            ActionType.RunScript => ("运行脚本", "📜"),
            ActionType.Composite => ("组合指令", "🔗"),
            _ => ("指令", "▶")
        };
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (TypeBox.SelectedIndex == 0)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择程序",
                Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                TargetBox.Text = dialog.FileName;
            }
        }
        else if (TypeBox.SelectedIndex == 2)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择文件夹" };
            if (dialog.ShowDialog() == true)
            {
                TargetBox.Text = dialog.FolderName;
            }
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var name = RunningAppsBox.SelectedItem is RunningApp app ? app.Name : RunningAppsBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("请先选择或输入一个软件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_config.Apps.Any(a => string.Equals(a.ProcessName, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("这个软件已经在列表里了。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var newApp = new AppProfile
        {
            ProcessName = name,
            Enabled = true,
            Actions = DefaultAppActions.Create().ToList()
        };
        _config.Apps.Add(newApp);

        ConfigStore.Save(_config);
        RunningAppsBox.Text = string.Empty;
        ReloadScenes();
        int target = 0;
        for (int i = 1; i < SceneList.Items.Count; i++)
        {
            if (SceneList.Items[i] is SceneItem { Profile: AppProfile profile } &&
                ReferenceEquals(profile, newApp))
            {
                target = i;
                break;
            }
        }
        SceneList.SelectedIndex = target;
    }

    private void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        if (_sceneIndex < 0)
        {
            MessageBox.Show("请先在左侧选择一个软件场景。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _config.Apps.RemoveAt(_sceneIndex);
        ConfigStore.Save(_config);
        _selectedApp = null;
        _sceneIndex = -1;
        _slotIndex = -1;
        ReloadScenes();
        SceneList.SelectedIndex = 0;
        UpdateEditorVisibility();
    }

    private void AppEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _sceneIndex < 0 || _sceneIndex >= _config.Apps.Count)
        {
            return;
        }

        _config.Apps[_sceneIndex].Enabled = AppEnabledCheck.IsChecked == true;
        ConfigStore.Save(_config);
        ReloadScenes();
    }

    private void AppFullscreenCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _sceneIndex < 0 || _sceneIndex >= _config.Apps.Count)
        {
            return;
        }

        _config.Apps[_sceneIndex].DisableOnFullscreen = AppFullscreenCheck.IsChecked;
        ConfigStore.Save(_config);
    }

    private void AppTriggerModeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _sceneIndex < 0 || _sceneIndex >= _config.Apps.Count)
        {
            return;
        }

        _config.Apps[_sceneIndex].TriggerMode = AppTriggerModeBox.SelectedIndex switch
        {
            1 => "Middle",
            2 => "Right",
            3 => "CtrlMiddle",
            _ => null
        };
        ConfigStore.Save(_config);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back)
        {
            // 如果焦点在文本输入控件中，允许正常删字，不拦截为清空格子
            if (Keyboard.FocusedElement is TextBox or PasswordBox)
            {
                return;
            }

            if (_slotIndex >= 0)
            {
                ClearCurrentSlot();
                e.Handled = true;
            }
        }
    }
}
