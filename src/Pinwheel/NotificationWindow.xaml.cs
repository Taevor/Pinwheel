using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pinwheel;

public enum NotificationType
{
    Success,
    Error,
    Info,
    Warning
}

public partial class NotificationWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _timer;
    private int _remainingMs = 3000;
    private bool _isClosing;
    private bool _isHovered;

    public NotificationWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _timer.Tick += Timer_Tick;

        MouseEnter += (_, _) => _isHovered = true;
        MouseLeave += (_, _) => _isHovered = false;
        Loaded += NotificationWindow_Loaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 设置无焦点激活与工具窗口属性，绝不打断用户当前打字和工作状态
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
    }

    public void Setup(string title, string message, NotificationType type, string? emojiIcon)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        // 根据通知类型配置色彩与图标
        switch (type)
        {
            case NotificationType.Success:
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xFA, 0xF0));
                IconBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xEA, 0xC8));
                StatusPath.Data = Geometry.Parse("M 9 16.2 L 4.8 12 L 3.4 13.4 L 9 19 L 21 7 L 19.6 5.6 L 9 16.2 Z");
                StatusPath.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x41));
                TimeoutProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x41));
                break;

            case NotificationType.Error:
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xEB, 0xEB));
                IconBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xBD, 0xBD));
                StatusPath.Data = Geometry.Parse("M 19 6.41 L 17.59 5 L 12 10.59 L 6.41 5 L 5 6.41 L 10.59 12 L 5 17.59 L 6.41 19 L 12 13.41 L 17.59 19 L 19 17.59 L 13.41 12 Z");
                StatusPath.Fill = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
                TimeoutProgress.Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
                break;

            case NotificationType.Warning:
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xE6));
                IconBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE7, 0x99));
                StatusPath.Data = Geometry.Parse("M 1 21 L 23 21 L 12 2 L 1 21 Z M 13 18 L 11 18 L 11 16 L 13 16 L 13 18 Z M 13 14 L 11 14 L 11 10 L 13 10 L 13 14 Z");
                StatusPath.Fill = new SolidColorBrush(Color.FromRgb(0x8A, 0x61, 0x00));
                TimeoutProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x61, 0x00));
                break;

            case NotificationType.Info:
            default:
                IconBadge.Background = new SolidColorBrush(Color.FromRgb(0xEB, 0xF3, 0xFC));
                IconBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBD, 0xD7, 0xF8));
                StatusPath.Data = Geometry.Parse("M 11 7 L 13 7 L 13 9 L 11 9 L 11 7 Z M 11 11 L 13 11 L 13 17 L 11 17 L 11 11 Z M 12 2 C 6.48 2 2 6.48 2 12 C 2 17.52 6.48 22 12 22 C 17.52 22 22 17.52 22 12 C 22 6.48 17.52 2 12 2 Z");
                StatusPath.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
                TimeoutProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
                break;
        }

        if (!string.IsNullOrWhiteSpace(emojiIcon) && type == NotificationType.Success)
        {
            EmojiIcon.Text = emojiIcon;
            EmojiIcon.Visibility = Visibility.Visible;
            StatusPath.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmojiIcon.Visibility = Visibility.Collapsed;
            StatusPath.Visibility = Visibility.Visible;
        }
    }

    private void NotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomRight();

        // 平滑淡入与向上微浮动动画
        Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        BeginAnimation(OpacityProperty, fadeIn);

        _timer.Start();
    }

    public void PositionAtBottomRight(int stackIndex = 0)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - (Height + 10) * (stackIndex + 1) - 6;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing) return;

        // 鼠标悬停时暂停倒计时
        if (!_isHovered)
        {
            _remainingMs -= 30;
            if (_remainingMs < 0) _remainingMs = 0;
            TimeoutProgress.Value = _remainingMs;

            if (_remainingMs <= 0)
            {
                Dismiss();
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
    }

    public void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _timer.Stop();

        // 优雅平滑淡出
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            try
            {
                Close();
            }
            catch
            {
            }
        };
        BeginAnimation(OpacityProperty, fadeOut);
    }
}

