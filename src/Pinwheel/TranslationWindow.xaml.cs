using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pinwheel;

public partial class TranslationWindow : Window
{
    private static TranslationWindow? _instance;
    private TranslationConfig _config;
    private bool _isKeyVisible = false;

    public static void ShowOrTranslate(string text = "")
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new TranslationWindow();
            }

            _instance.Show();
            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }
            _instance.Activate();

            if (!string.IsNullOrWhiteSpace(text))
            {
                _instance.TranslateAndAddCard(text);
            }
        });
    }

    public TranslationWindow()
    {
        InitializeComponent();

        var appConfig = ConfigStore.Load();
        _config = appConfig.Translation ?? new TranslationConfig();

        Topmost = _config.Topmost;
        UpdatePinIcon();

        // 恢复窗口尺寸
        if (_config.WindowWidth >= 380) Width = _config.WindowWidth;
        if (_config.WindowHeight >= 420) Height = _config.WindowHeight;

        LoadConfigToUi();

        // 首次使用检查：如果未配置 Key 且非免费模式，自动展开配置卡片引导用户
        bool hasKey = !string.IsNullOrWhiteSpace(_config.ApiKey) || string.Equals(_config.Provider, "Free", StringComparison.OrdinalIgnoreCase);
        if (!hasKey)
        {
            ConfigDrawer.Visibility = Visibility.Visible;
            ConfigHintText.Text = "👈 请先配置 API Key 即可开始使用";
            StatusBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusBadge.Visibility = Visibility.Visible;
            StatusBadgeText.Text = _config.Provider;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // 记住窗口尺寸
        _config.WindowWidth = Width;
        _config.WindowHeight = Height;
        SaveAppConfig();

        // 手动关闭
        Hide();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _config.Topmost = Topmost;
        SaveAppConfig();
        UpdatePinIcon();
    }

    private void UpdatePinIcon()
    {
        if (Topmost)
        {
            PinButton.Background = (Brush)FindResource("FluentAccentLight");
            PinButton.ToolTip = "已置顶（点击取消）";
        }
        else
        {
            PinButton.Background = Brushes.Transparent;
            PinButton.ToolTip = "点击置顶窗口";
        }
    }

    private void ConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ConfigDrawer.Visibility = ConfigDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        CardsContainer.Children.Clear();
        EmptyPlaceholder.Visibility = Visibility.Visible;
    }

    private void ManualTranslate_Click(object sender, RoutedEventArgs e)
    {
        string text = ManualInputBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            ManualInputBox.Text = "";
            TranslateAndAddCard(text);
        }
    }

    private void ManualInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ManualTranslate_Click(sender, e);
        }
    }

    public async void TranslateAndAddCard(string text)
    {
        text = text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;

        EmptyPlaceholder.Visibility = Visibility.Collapsed;

        // 检查配置
        bool isFree = string.Equals(_config.Provider, "Free", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_config.ApiKey) && !isFree)
        {
            ConfigDrawer.Visibility = Visibility.Visible;
            ConfigHintText.Text = "👈 请配置 API Key 后再试";
        }

        // 创建卡片
        var card = CreateCardElement(text);
        CardsContainer.Children.Insert(0, card.Container); // 最新翻译插入在最上方
        CardScrollViewer.ScrollToTop();

        // 异步执行翻译
        card.LoadingIndicator.Visibility = Visibility.Visible;
        card.TranslatedBox.Text = "正在翻译中…";
        card.TranslatedBox.Opacity = 0.6;

        var result = await TranslationService.TranslateAsync(text, _config);

        card.LoadingIndicator.Visibility = Visibility.Collapsed;
        card.TranslatedBox.Opacity = 1.0;

        if (result.IsSuccess)
        {
            card.TranslatedBox.Text = result.TranslatedText;
            card.ProviderLabel.Text = result.Provider;
        }
        else
        {
            card.TranslatedBox.Text = result.ErrorMessage;
            card.TranslatedBox.Foreground = (Brush)FindResource("FluentDanger");
        }
    }

    private CardHolder CreateCardElement(string originalText)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("FluentCardBackground"),
            BorderBrush = (Brush)FindResource("FluentCardBorderSubtle"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var mainPanel = new StackPanel();

        // 卡片顶部：时间与工具
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var timeLabel = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm:ss"),
            FontSize = 11,
            Foreground = (Brush)FindResource("FluentTextTertiary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var providerBadge = new Border
        {
            Background = (Brush)FindResource("FluentCardBackgroundSecondary"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2)
        };
        var providerText = new TextBlock
        {
            Text = _config.Provider,
            FontSize = 10,
            Foreground = (Brush)FindResource("FluentTextSecondary")
        };
        providerBadge.Child = providerText;
        infoPanel.Children.Add(timeLabel);
        infoPanel.Children.Add(providerBadge);

        // 卡片右侧按钮
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var copyBtn = new Button
        {
            Content = "复制",
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            Style = (Style)FindResource("FluentSecondaryButton")
        };
        var delBtn = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            FontSize = 10,
            Style = (Style)FindResource("FluentGhostButton")
        };
        btnPanel.Children.Add(copyBtn);
        btnPanel.Children.Add(delBtn);

        topGrid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 0);
        topGrid.Children.Add(btnPanel);
        Grid.SetColumn(btnPanel, 1);

        // 原文展示
        var originalBlock = new TextBlock
        {
            Text = originalText,
            FontSize = 12,
            Foreground = (Brush)FindResource("FluentTextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 分割线
        var sep = new Border
        {
            Height = 1,
            Background = (Brush)FindResource("FluentCardBorderSubtle"),
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 译文展示
        var transGrid = new Grid();
        var transBlock = new TextBlock
        {
            FontSize = 13.5,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("FluentTextPrimary"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        var loadingBlock = new TextBlock
        {
            Text = "⏳",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        transGrid.Children.Add(transBlock);
        transGrid.Children.Add(loadingBlock);

        mainPanel.Children.Add(topGrid);
        mainPanel.Children.Add(originalBlock);
        mainPanel.Children.Add(sep);
        mainPanel.Children.Add(transGrid);

        border.Child = mainPanel;

        // 事件绑定
        copyBtn.Click += (s, e) =>
        {
            try
            {
                Clipboard.SetText(transBlock.Text);
                copyBtn.Content = "已复制 ✓";
                copyBtn.Foreground = (Brush)FindResource("FluentAccent");
            }
            catch { }
        };

        delBtn.Click += (s, e) =>
        {
            CardsContainer.Children.Remove(border);
            if (CardsContainer.Children.Count == 0)
            {
                EmptyPlaceholder.Visibility = Visibility.Visible;
            }
        };

        return new CardHolder
        {
            Container = border,
            TranslatedBox = transBlock,
            LoadingIndicator = loadingBlock,
            ProviderLabel = providerText
        };
    }

    private sealed class CardHolder
    {
        public Border Container { get; set; } = null!;
        public TextBlock TranslatedBox { get; set; } = null!;
        public TextBlock LoadingIndicator { get; set; } = null!;
        public TextBlock ProviderLabel { get; set; } = null!;
    }

    #region 配置面板逻辑

    private void LoadConfigToUi()
    {
        // 选中 Provider
        foreach (ComboBoxItem item in ProviderComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), _config.Provider, StringComparison.OrdinalIgnoreCase))
            {
                ProviderComboBox.SelectedItem = item;
                break;
            }
        }

        ApiKeyBox.Password = _config.ApiKey ?? "";
        ApiKeyTextBox.Text = _config.ApiKey ?? "";
        BaseUrlBox.Text = _config.BaseUrl ?? "";
        ModelBox.Text = _config.Model ?? "";

        UpdateProviderUi();
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            string tag = item.Tag.ToString()!;
            switch (tag)
            {
                case "DeepSeek":
                    BaseUrlBox.Text = "https://api.deepseek.com/v1";
                    ModelBox.Text = "deepseek-chat";
                    break;
                case "SiliconFlow":
                    BaseUrlBox.Text = "https://api.siliconflow.cn/v1";
                    ModelBox.Text = "deepseek-ai/DeepSeek-V3";
                    break;
                case "OpenAI":
                    BaseUrlBox.Text = "https://api.openai.com/v1";
                    ModelBox.Text = "gpt-4o-mini";
                    break;
                case "Free":
                    BaseUrlBox.Text = "";
                    ModelBox.Text = "";
                    break;
            }
            UpdateProviderUi();
        }
    }

    private void UpdateProviderUi()
    {
        if (ProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            bool isFree = string.Equals(item.Tag.ToString(), "Free", StringComparison.OrdinalIgnoreCase);
            ApiKeyRow.Visibility = isFree ? Visibility.Collapsed : Visibility.Visible;
            AdvancedRow.Visibility = isFree ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void ToggleKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isKeyVisible = !_isKeyVisible;
        if (_isKeyVisible)
        {
            ApiKeyTextBox.Text = ApiKeyBox.Password;
            ApiKeyBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            KeyEyeIcon.Text = "🔒";
        }
        else
        {
            ApiKeyBox.Password = ApiKeyTextBox.Text;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyBox.Visibility = Visibility.Visible;
            KeyEyeIcon.Text = "👁";
        }
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _config.Provider = item.Tag.ToString()!;
        }

        string key = _isKeyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        _config.ApiKey = key;
        _config.BaseUrl = BaseUrlBox.Text.Trim();
        _config.Model = ModelBox.Text.Trim();

        SaveAppConfig();

        StatusBadge.Visibility = Visibility.Visible;
        StatusBadgeText.Text = _config.Provider;
        ConfigDrawer.Visibility = Visibility.Collapsed;
        ConfigHintText.Text = "";

        NotificationService.ShowSuccess("Pinwheel 翻译", "API 配置已保存成功！");
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var testConfig = new TranslationConfig();
        if (ProviderComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            testConfig.Provider = item.Tag.ToString()!;
        }
        testConfig.ApiKey = _isKeyVisible ? ApiKeyTextBox.Text.Trim() : ApiKeyBox.Password.Trim();
        testConfig.BaseUrl = BaseUrlBox.Text.Trim();
        testConfig.Model = ModelBox.Text.Trim();

        ConfigHintText.Text = "正在测试连接中…";
        var res = await TranslationService.TranslateAsync("Hello, welcome to Pinwheel!", testConfig);
        if (res.IsSuccess)
        {
            ConfigHintText.Text = $"✓ 连接成功：{res.TranslatedText}";
        }
        else
        {
            ConfigHintText.Text = $"✗ 失败：{res.ErrorMessage}";
        }
    }

    private void SaveAppConfig()
    {
        try
        {
            var appConfig = ConfigStore.Load();
            appConfig.Translation = _config;
            ConfigStore.Save(appConfig);
        }
        catch { }
    }

    #endregion
}

