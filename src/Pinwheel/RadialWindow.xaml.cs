using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pinwheel;

public partial class RadialWindow : Window
{
    // 鼠标移入扇区（Hover）小巧微弹果冻缩放比例（比原尺寸仅微弹 7%，精致不突兀）
    private const double HoverScale = 1.07;
    private const double SafetyMargin = 32; // 直径两端适度留白（共 32px，半径各 16px），容纳扇区 Hover 放大与微阴影

    // 扇区 Hover / Unhover 果冻微弹缓动
    private static readonly IEasingFunction HoverJellyEase = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction QuickEaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly Duration HoverAnimDuration = new(TimeSpan.FromMilliseconds(95));
    private static readonly Duration IdleAnimDuration = new(TimeSpan.FromMilliseconds(85));

    private double _diameter = 380;
    private double _center = 206; // (380 + SafetyMargin) / 2
    private double _innerInner = 40;
    private double _innerOuter = 92;
    private double _outerInner = 100;
    private double _outerOuter = 158;
    private double _innerGap = 6;
    private double _outerGap = 4;
    private string _currentSize = "";
    private string _currentTheme = "";

    // 主题色彩（默认 Fluent 蓝经典实体白底版）
    private Color _idleColor = Color.FromRgb(0xFA, 0xFA, 0xFC);
    private Color _hoverColor = Color.FromRgb(0xDE, 0xEC, 0xF9);
    private Color _strokeColor = Color.FromArgb(0x35, 0x00, 0x00, 0x00);
    private Color _centerColor = Color.FromRgb(0x00, 0x67, 0xC0);
    private Color _labelColor = Color.FromRgb(0x1A, 0x1A, 0x1A);

    private readonly List<SliceSlot> _slots = new(24);
    private readonly DispatcherTimer _errorTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly ScaleTransform _rootScale = new(1, 1);

    // 常驻基础装饰元素
    private Ellipse _shadowEllipse = null!;
    private Ellipse _centerHalo = null!;
    private Ellipse _centerDot = null!;
    private Ellipse _innerCore = null!;
    private Ellipse _errorDot = null!;

    private bool _isRenderingHooked;
    private int _hoveredIndex = -1;
    private bool _isClosing;

    public RadialWindow()
    {
        InitializeComponent();

        Left = -10000;
        Top = -10000;

        RootCanvas.RenderTransform = _rootScale;

        // 监听窗体本地鼠标移动，与逐帧渲染双重保障 0 延迟响应
        MouseMove += OnWindowMouseMove;

        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer.Stop();
            _errorDot.Visibility = Visibility.Collapsed;
            HideRadial();
        };

        // 一次性构建持久化视觉树
        BuildPersistentVisualTree();
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsVisible || _isClosing) return;
        var p = e.GetPosition(this);
        CheckHoverAtLocalPoint(p);
    }

    /// <summary>
    /// 一次性构建 24 个扇区槽位与中心装饰元素，避免每次呼出时重复创建/销毁 WPF 控件
    /// </summary>
    private void BuildPersistentVisualTree()
    {
        RootCanvas.Children.Clear();
        _slots.Clear();

        ApplyLayoutMetrics(380);

        // 1. 底层微软阴影（紧贴轮盘边缘，极淡柔和，0ms GPU 满帧渲染）
        _shadowEllipse = new Ellipse
        {
            IsHitTestVisible = false
        };
        UpdateShadowBrush();
        Canvas.SetZIndex(_shadowEllipse, -10);
        RootCanvas.Children.Add(_shadowEllipse);

        // 2. 内圈 8 个固定槽位
        for (int i = 0; i < 8; i++)
        {
            double mid = -90 + i * 45;
            var slot = CreateSlot(i, mid, 45, isOuter: false);
            _slots.Add(slot);
        }

        // 3. 外圈 16 个固定槽位
        for (int i = 0; i < 16; i++)
        {
            double mid = -90 + i * 22.5;
            var slot = CreateSlot(8 + i, mid, 22.5, isOuter: true);
            _slots.Add(slot);
        }

        // 4. 中心 Fluent 微光环与圆点
        _centerHalo = new Ellipse
        {
            Width = 20,
            Height = 20,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_centerHalo, 99);
        RootCanvas.Children.Add(_centerHalo);

        _centerDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_centerDot, 100);
        RootCanvas.Children.Add(_centerDot);

        _innerCore = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_innerCore, 101);
        RootCanvas.Children.Add(_innerCore);

        // 5. 错误提示红点（常驻覆盖层）
        _errorDot = new Ellipse
        {
            Width = 36,
            Height = 36,
            Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x45, 0x45)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_errorDot, 200);
        RootCanvas.Children.Add(_errorDot);

        UpdateCenterElementsLayout();
    }

    private SliceSlot CreateSlot(int index, double midDeg, double span, bool isOuter)
    {
        var fillBrush = new SolidColorBrush(_idleColor);
        var strokeBrush = new SolidColorBrush(_strokeColor);
        var sliceScale = new ScaleTransform(1, 1, _center, _center);

        var path = new Path
        {
            Fill = fillBrush,
            Stroke = strokeBrush,
            StrokeThickness = 1.2,
            RenderTransform = sliceScale
        };
        Canvas.SetZIndex(path, 0);
        RootCanvas.Children.Add(path);

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };

        var iconBlock = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(iconBlock);

        var nameBlock = new TextBlock
        {
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI, sans-serif"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        panel.Children.Add(nameBlock);

        var labelScale = new ScaleTransform(1, 1);
        panel.RenderTransform = labelScale;
        Canvas.SetZIndex(panel, 20);
        RootCanvas.Children.Add(panel);

        var slot = new SliceSlot
        {
            Index = index,
            IsOuter = isOuter,
            MidDeg = midDeg,
            Span = span,
            SlicePath = path,
            FillBrush = fillBrush,
            StrokeBrush = strokeBrush,
            SliceScale = sliceScale,
            LabelPanel = panel,
            IconBlock = iconBlock,
            NameBlock = nameBlock,
            LabelScale = labelScale
        };

        UpdateSlotGeometry(slot);
        return slot;
    }

    /// <summary>
    /// 背景阴影与亚克力漫射光晕渲染：
    /// - 亚克力毛玻璃：独享超柔漫反射冰晶光环，将后方桌面自然雾化
    /// - 经典白底：精细微软微投影
    /// </summary>
    private void UpdateShadowBrush()
    {
        if (_currentTheme == "Glass")
        {
            _shadowEllipse.Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x85, 0xF5, 0xF8, 0xFD), 0.0),    // 中心磨砂柔光
                    new GradientStop(Color.FromArgb(0x60, 0xEB, 0xF2, 0xFB), 0.70),   // 冰晶过渡
                    new GradientStop(Color.FromArgb(0x35, 0xBC, 0xD7, 0xFA), 0.88),   // 亚克力冰蓝微光光晕
                    new GradientStop(Color.FromArgb(0x18, 0x00, 0x67, 0xC0), 0.95),   // 浅蓝倒角轮廓
                    new GradientStop(Color.FromArgb(0x00, 0x00, 0, 0), 1.0)           // 平滑隐入桌面
                }
            };
        }
        else if (_currentTheme == "Dark")
        {
            _shadowEllipse.Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.80),
                    new GradientStop(Color.FromArgb(0x40, 0x00, 0x00, 0x00), 0.94),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
                }
            };
        }
        else
        {
            _shadowEllipse.Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.84),
                    new GradientStop(Color.FromArgb(0x18, 0, 0, 0), 0.94),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
                }
            };
        }
    }

    private void ApplyLayoutMetrics(double diameter)
    {
        _diameter = diameter;
        double windowSize = _diameter + SafetyMargin;
        _center = windowSize / 2;

        _innerInner = diameter * 0.105;
        _innerOuter = diameter * 0.242;
        _outerInner = diameter * 0.263;
        _outerOuter = diameter * 0.416;
        _innerGap = 6 * diameter / 380;
        _outerGap = 4 * diameter / 380;

        Width = windowSize;
        Height = windowSize;

        _rootScale.CenterX = _center;
        _rootScale.CenterY = _center;
    }

    private void UpdateCenterElementsLayout()
    {
        // 阴影大小紧贴最外圈扇区边缘，并向外延展柔和漫射
        double shadowR = _outerOuter + 22;
        double shadowDiam = shadowR * 2;
        Canvas.SetLeft(_shadowEllipse, _center - shadowR);
        Canvas.SetTop(_shadowEllipse, _center - shadowR);
        _shadowEllipse.Width = shadowDiam;
        _shadowEllipse.Height = shadowDiam;

        Canvas.SetLeft(_centerHalo, _center - 10);
        Canvas.SetTop(_centerHalo, _center - 10);
        _centerHalo.Fill = new SolidColorBrush(Color.FromArgb(0x35, _centerColor.R, _centerColor.G, _centerColor.B));

        Canvas.SetLeft(_centerDot, _center - 6);
        Canvas.SetTop(_centerDot, _center - 6);
        _centerDot.Fill = new SolidColorBrush(_centerColor);

        Canvas.SetLeft(_innerCore, _center - 2);
        Canvas.SetTop(_innerCore, _center - 2);

        Canvas.SetLeft(_errorDot, _center - 18);
        Canvas.SetTop(_errorDot, _center - 18);
    }

    private void UpdateSlotGeometry(SliceSlot slot)
    {
        double innerR = slot.IsOuter ? _outerInner : _innerInner;
        double outerR = slot.IsOuter ? _outerOuter : _innerOuter;
        double gap = slot.IsOuter ? _outerGap : _innerGap;

        slot.SlicePath.Data = CreateSliceGeometry(slot.MidDeg, slot.Span, innerR, outerR, gap);
        slot.SliceScale.CenterX = _center;
        slot.SliceScale.CenterY = _center;

        double scale = _diameter / 380;
        double panelWidth = (slot.IsOuter ? 38 : 44) * scale;
        slot.LabelPanel.Width = panelWidth;

        var pos = PointOnCircle(slot.MidDeg, (innerR + outerR) / 2);
        double estHeight = (slot.IsOuter ? 30 : 36) * scale;
        double panelX = pos.X - panelWidth / 2;
        double panelY = pos.Y - estHeight / 2;

        Canvas.SetLeft(slot.LabelPanel, panelX);
        Canvas.SetTop(slot.LabelPanel, panelY);

        slot.LabelScale.CenterX = _center - panelX;
        slot.LabelScale.CenterY = _center - panelY;
    }

    public void ShowAt(
        System.Windows.Point screenPoint,
        IReadOnlyList<RadialAction> globalActions,
        IReadOnlyList<RadialAction> appActions,
        string size,
        string theme)
    {
        _isClosing = false;
        _errorTimer.Stop();
        _errorDot.Visibility = Visibility.Collapsed;

        // 1. 尺寸变更检测与几何更新
        if (_currentSize != size)
        {
            _currentSize = size;
            if (double.TryParse(size, out double diameter) && diameter >= 200 && diameter <= 1000)
            {
                ApplyLayoutMetrics(diameter);
            }
            else
            {
                ApplyLayoutMetrics(380);
            }

            foreach (var slot in _slots)
            {
                UpdateSlotGeometry(slot);
            }
            UpdateCenterElementsLayout();
        }

        // 2. 主题变更检测与全扇片画刷即时刷新
        if (_currentTheme != theme)
        {
            _currentTheme = theme;
            ApplyTheme(theme);
        }

        // 3. 极速数据映射：直接复用已有 24 个槽位，0ms 零内存分配
        var labelBrush = new SolidColorBrush(_labelColor);
        labelBrush.Freeze();
        double scale = _diameter / 380;

        for (int i = 0; i < 8; i++)
        {
            var action = i < appActions.Count ? appActions[i] : null;
            if (action != null && action.IsBlank) action = null;
            _slots[i].BindAction(action, labelBrush, scale);
        }

        for (int i = 0; i < 16; i++)
        {
            RadialAction? action = i < 8
                ? (8 + i < appActions.Count ? appActions[8 + i] : null)
                : (15 - i < globalActions.Count ? globalActions[15 - i] : null);
            if (action != null && action.IsBlank) action = null;
            _slots[8 + i].BindAction(action, labelBrush, scale);
        }

        // 4. 重置 Hover 状态并定位
        _hoveredIndex = -1;
        ResetHoverImmediate();
        PositionWindow(screenPoint);

        // 5. 瞬间弹出（零延迟呈现，不耽误任何操作时间）
        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootCanvas.BeginAnimation(UIElement.OpacityProperty, null);
        _rootScale.ScaleX = 1.0;
        _rootScale.ScaleY = 1.0;
        RootCanvas.Opacity = 1.0;

        Show();
        StartRenderingHook();
    }

    private void PositionWindow(System.Windows.Point screenPoint)
    {
        double scale = GetDpiForSystem() / 96.0;
        var dipPoint = new Point(screenPoint.X / scale, screenPoint.Y / scale);

        double left = dipPoint.X - _center;
        double top = dipPoint.Y - _center;

        var workArea = SystemParameters.WorkArea;
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));

        Left = left;
        Top = top;
    }

    public void FlashError(System.Windows.Point screenPoint)
    {
        StopRenderingHook();
        _hoveredIndex = -1;

        // 隐藏所有扇区，仅展示居中错误红点
        foreach (var slot in _slots)
        {
            slot.SlicePath.Visibility = Visibility.Collapsed;
            slot.LabelPanel.Visibility = Visibility.Collapsed;
        }

        _errorDot.Visibility = Visibility.Visible;
        PositionWindow(screenPoint);
        Show();

        _errorTimer.Stop();
        _errorTimer.Start();
    }

    public RadialAction? GetActionAt(System.Windows.Point screenPoint)
    {
        double scale = GetDpiForSystem() / 96.0;
        var local = new Point(screenPoint.X / scale - Left, screenPoint.Y / scale - Top);
        int index = GetUnifiedIndex(local);
        if (index >= 0 && index < _slots.Count)
        {
            var action = _slots[index].Action;
            return (action != null && !action.IsBlank) ? action : null;
        }
        return null;
    }

    /// <summary>
    /// 取消/关闭时的瞬间隐藏
    /// </summary>
    public void HideRadial()
    {
        StopRenderingHook();
        _hoveredIndex = -1;
        _isClosing = false;
        Hide();

        _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        RootCanvas.BeginAnimation(UIElement.OpacityProperty, null);
        _rootScale.ScaleX = 1.0;
        _rootScale.ScaleY = 1.0;
        RootCanvas.Opacity = 1.0;
    }

    private void StartRenderingHook()
    {
        if (!_isRenderingHooked)
        {
            CompositionTarget.Rendering += OnRenderingFrame;
            _isRenderingHooked = true;
        }
    }

    private void StopRenderingHook()
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnRenderingFrame;
            _isRenderingHooked = false;
        }
    }

    /// <summary>
    /// 逐帧垂直同步（60Hz/144Hz）监听光标位置
    /// </summary>
    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!IsVisible || _isClosing)
        {
            return;
        }

        GetCursorPos(out POINT pt);
        double scale = GetDpiForSystem() / 96.0;
        var local = new Point(pt.X / scale - Left, pt.Y / scale - Top);
        CheckHoverAtLocalPoint(local);
    }

    private void CheckHoverAtLocalPoint(Point local)
    {
        int index = GetUnifiedIndex(local);
        if (index == _hoveredIndex)
        {
            return;
        }

        int previous = _hoveredIndex;
        _hoveredIndex = index;

        if (previous >= 0 && previous < _slots.Count)
        {
            Canvas.SetZIndex(_slots[previous].SlicePath, 0);
            AnimateSliceIdle(_slots[previous], _idleColor, _strokeColor);
        }

        if (index >= 0 && index < _slots.Count)
        {
            var slot = _slots[index];
            if (slot.Action != null && !slot.Action.IsBlank)
            {
                Canvas.SetZIndex(slot.SlicePath, 10);
                AnimateSliceHover(slot, _hoverColor, _centerColor, HoverScale);
            }
        }
    }

    private int GetUnifiedIndex(Point point)
    {
        double dx = point.X - _center;
        double dy = point.Y - _center;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        angle = (angle + 360) % 360;

        if (distance >= _innerInner && distance <= _innerOuter)
        {
            double shifted = (angle - 270 + 360 + 22.5) % 360;
            return (int)(shifted / 45);
        }

        if (distance >= _outerInner && distance <= _outerOuter)
        {
            double shifted = (angle - 270 + 360 + 11.25) % 360;
            return 8 + (int)(shifted / 22.5);
        }

        return -1;
    }

    private void ResetHoverImmediate()
    {
        foreach (var slot in _slots)
        {
            Canvas.SetZIndex(slot.SlicePath, 0);
            slot.FillBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            slot.StrokeBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            slot.SliceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            slot.SliceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            slot.LabelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            slot.LabelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            slot.FillBrush.Color = _idleColor;
            slot.StrokeBrush.Color = _strokeColor;
            slot.SliceScale.ScaleX = 1.0;
            slot.SliceScale.ScaleY = 1.0;
            slot.LabelScale.ScaleX = 1.0;
            slot.LabelScale.ScaleY = 1.0;
        }
    }

    /// <summary>
    /// 鼠标移入扇区（Hover）：轻微过冲果冻微弹（幅度小巧精致，清脆灵动）
    /// </summary>
    private static void AnimateSliceHover(SliceSlot slot, Color targetFill, Color targetStroke, double targetScale)
    {
        var fillAnim = new ColorAnimation(targetFill, HoverAnimDuration) { EasingFunction = QuickEaseOut };
        var strokeAnim = new ColorAnimation(targetStroke, HoverAnimDuration) { EasingFunction = QuickEaseOut };
        var scaleAnim = new DoubleAnimation(targetScale, HoverAnimDuration) { EasingFunction = HoverJellyEase };

        slot.FillBrush.BeginAnimation(SolidColorBrush.ColorProperty, fillAnim);
        slot.StrokeBrush.BeginAnimation(SolidColorBrush.ColorProperty, strokeAnim);

        slot.SliceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        slot.SliceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        slot.LabelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        slot.LabelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    /// <summary>
    /// 鼠标移出扇区（Unhover）：清脆平滑收回原位
    /// </summary>
    private static void AnimateSliceIdle(SliceSlot slot, Color targetFill, Color targetStroke)
    {
        var fillAnim = new ColorAnimation(targetFill, IdleAnimDuration) { EasingFunction = QuickEaseOut };
        var strokeAnim = new ColorAnimation(targetStroke, IdleAnimDuration) { EasingFunction = QuickEaseOut };
        var scaleAnim = new DoubleAnimation(1.0, IdleAnimDuration) { EasingFunction = QuickEaseOut };

        slot.FillBrush.BeginAnimation(SolidColorBrush.ColorProperty, fillAnim);
        slot.StrokeBrush.BeginAnimation(SolidColorBrush.ColorProperty, strokeAnim);

        slot.SliceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        slot.SliceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        slot.LabelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        slot.LabelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    /// <summary>
    /// 主题体系：
    /// - 经典白底（默认 Fluent 蓝 / 纯白）：实色纯白扎实基底，超清抗锯齿文字与微投影
    /// - 亚克力毛玻璃：全新实现晶莹磨砂透光玻璃、高亮反光倒角边与背景漫反射雾化盘
    /// - 深邃暗黑：暗夜模式深灰底色与柔白字
    /// </summary>
    private void ApplyTheme(string theme)
    {
        (_idleColor, _hoverColor, _strokeColor, _centerColor, _labelColor) = theme switch
        {
            "Glass" => (
                Color.FromArgb(0x52, 0xFA, 0xFD, 0xFF), // 亚克力毛玻璃：通透磨砂冰晶（~32% 不透明度）
                Color.FromArgb(0xDD, 0xDE, 0xEC, 0xF9), // 悬停：Win11 透光冰蓝
                Color.FromArgb(0x95, 0xFF, 0xFF, 0xFF), // 边缘：高亮折射反光倒角光（晶莹微晶亮边）
                Color.FromArgb(0xFF, 0x00, 0x67, 0xC0), // 中心：Fluent 宝石蓝
                Color.FromArgb(0xFF, 0x11, 0x18, 0x27)),// 文字：深黑极清抗锯齿

            "White" => (
                Color.FromRgb(0xFF, 0xFF, 0xFF),         // 纯白实体不透明
                Color.FromRgb(0xF0, 0xF0, 0xF3),         // 悬停浅灰
                Color.FromArgb(0x28, 0x00, 0x00, 0x00), // 微细描边
                Color.FromRgb(0x00, 0x67, 0xC0),
                Color.FromRgb(0x1A, 0x1A, 0x1A)),

            "Dark" => (
                Color.FromRgb(0x28, 0x2A, 0x2E),         // 深邃暗夜实色底
                Color.FromRgb(0x3B, 0x47, 0x58),         // 悬停深海青灰
                Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF), // 暗色下亮边
                Color.FromRgb(0x4C, 0x9A, 0xF0),         // 亮蓝中心
                Color.FromRgb(0xF3, 0xF4, 0xF6)),        // 纯白清晰文字

            _ => (
                Color.FromRgb(0xFA, 0xFA, 0xFC),         // 经典白底（默认 Fluent 蓝）：实体柔白不透明
                Color.FromRgb(0xDE, 0xEC, 0xF9),         // 悬停淡天蓝
                Color.FromArgb(0x35, 0x00, 0x00, 0x00), // 经典描边
                Color.FromRgb(0x00, 0x67, 0xC0),         // Fluent 品牌蓝
                Color.FromRgb(0x1A, 0x1A, 0x1A))         // 经典文字黑
        };

        UpdateShadowBrush();

        // 立即刷新所有槽位画刷，确保切换主题时立刻生效！
        foreach (var slot in _slots)
        {
            slot.FillBrush.Color = _idleColor;
            slot.StrokeBrush.Color = _strokeColor;
        }

        UpdateCenterElementsLayout();
    }

    private Geometry CreateSliceGeometry(double midDeg, double span, double innerRadius, double outerRadius, double gapWidth)
    {
        double start = midDeg - span / 2;
        double end = midDeg + span / 2;
        double halfGap = gapWidth / 2;

        var outerStart = PointOnStraightEdge(start, +halfGap, outerRadius);
        var innerStart = PointOnStraightEdge(start, +halfGap, innerRadius);
        var innerEnd = PointOnStraightEdge(end, -halfGap, innerRadius);
        var outerEnd = PointOnStraightEdge(end, -halfGap, outerRadius);

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private Point PointOnStraightEdge(double boundaryDeg, double signedOffset, double radius)
    {
        double radians = boundaryDeg * Math.PI / 180;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double along = Math.Sqrt(Math.Max(0, radius * radius - signedOffset * signedOffset));

        double x = _center - signedOffset * sin + along * cos;
        double y = _center + signedOffset * cos + along * sin;
        return new Point(x, y);
    }

    private Point PointOnCircle(double degrees, double radius)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(_center + radius * Math.Cos(radians), _center + radius * Math.Sin(radians));
    }

    private static double GetInnerFontSize(string text)
    {
        int length = text.Length;
        if (length <= 2) return 10.5;
        if (length <= 4) return 9.5;
        return 8.5;
    }

    private static double GetOuterFontSize(string text)
    {
        int length = text.Length;
        if (length <= 2) return 9.5;
        if (length <= 4) return 8.5;
        return 8.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// 单个持久化扇区槽位，维持不变的视觉树结构，呼出时仅动态填充数据
    /// </summary>
    private sealed class SliceSlot
    {
        public int Index { get; init; }
        public bool IsOuter { get; init; }
        public double MidDeg { get; init; }
        public double Span { get; init; }

        public Path SlicePath { get; init; } = null!;
        public SolidColorBrush FillBrush { get; init; } = null!;
        public SolidColorBrush StrokeBrush { get; init; } = null!;
        public ScaleTransform SliceScale { get; init; } = null!;

        public StackPanel LabelPanel { get; init; } = null!;
        public TextBlock IconBlock { get; init; } = null!;
        public TextBlock NameBlock { get; init; } = null!;
        public ScaleTransform LabelScale { get; init; } = null!;

        public RadialAction? Action { get; private set; }

        public void BindAction(RadialAction? action, Brush labelBrush, double scale)
        {
            Action = action;
            SlicePath.Visibility = Visibility.Visible;
            LabelPanel.Visibility = Visibility.Visible;

            if (action == null || action.IsBlank)
            {
                IconBlock.Text = "";
                NameBlock.Text = "";
                SlicePath.Opacity = 0.28; // 空白槽位维持低饱和度极简轮廓
            }
            else
            {
                SlicePath.Opacity = 1.0;
                IconBlock.Text = action.Icon;
                IconBlock.FontSize = (IsOuter ? 12.5 : 15.0) * scale;
                IconBlock.Foreground = labelBrush;
                IconBlock.Margin = new Thickness(0, 0, 0, 1.5 * scale);

                NameBlock.Text = action.Name;
                NameBlock.FontSize = (IsOuter ? GetOuterFontSize(action.Name) : GetInnerFontSize(action.Name)) * scale;
                NameBlock.Foreground = labelBrush;
                NameBlock.MaxHeight = (IsOuter ? 22 : 26) * scale;
                NameBlock.LineHeight = (IsOuter ? 10.5 : 12.0) * scale;
                NameBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
        }
    }
}
