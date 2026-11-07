using System;
using System.Collections.Generic;
using System.Windows;

namespace Pinwheel;

public static class NotificationService
{
    private static readonly List<NotificationWindow> ActiveWindows = new();

    public static void ShowSuccess(string title, string message, string? emojiIcon = null)
    {
        Show(title, message, NotificationType.Success, emojiIcon);
    }

    public static void ShowError(string title, string message)
    {
        Show(title, message, NotificationType.Error, null);
    }

    public static void ShowInfo(string title, string message, string? emojiIcon = null)
    {
        Show(title, message, NotificationType.Info, emojiIcon);
    }

    public static void ShowWarning(string title, string message, string? emojiIcon = null)
    {
        Show(title, message, NotificationType.Warning, emojiIcon);
    }

    public static void Show(string title, string message, NotificationType type = NotificationType.Success, string? emojiIcon = null)
    {
        if (Application.Current is null) return;

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // 清理已关闭的窗口引用
                ActiveWindows.RemoveAll(w => !w.IsLoaded);

                // 如果已有超过 3 个通知，先关闭最早的一个
                if (ActiveWindows.Count >= 3)
                {
                    ActiveWindows[0].Dismiss();
                    ActiveWindows.RemoveAt(0);
                }

                var window = new NotificationWindow();
                window.Setup(title, message, type, emojiIcon);

                int stackIndex = ActiveWindows.Count;
                window.PositionAtBottomRight(stackIndex);

                window.Closed += (_, _) =>
                {
                    ActiveWindows.Remove(window);
                    // 重新对齐剩下的通知
                    for (int i = 0; i < ActiveWindows.Count; i++)
                    {
                        ActiveWindows[i].PositionAtBottomRight(i);
                    }
                };

                ActiveWindows.Add(window);
                window.Show();
            }
            catch
            {
            }
        });
    }
}

