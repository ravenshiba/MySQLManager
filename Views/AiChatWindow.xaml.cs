using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class AiChatWindow : Window
{
    private readonly AiSqlService         _ai  = App.AiSqlService;
    private readonly List<AiChatMessage>  _history = new();
    public  string? CurrentDatabase { get; set; }
    public  event Action<string>? InsertSqlRequested;

    public AiChatWindow() { InitializeComponent(); Loaded += (_, _) => App.FitWindowToScreen(this); }

    // ── 傳送訊息 ──────────────────────────────────────────────

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        { e.Handled = true; await SendAsync(); }
    }

    private async System.Threading.Tasks.Task SendAsync()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!_ai.IsConfigured)
        {
            AppendMessage(AiChatMessage.FromAssistant("❌ 請先設定 Groq API Key。\n點擊右上角「⚙️ API Key」按鈕設定。"));
            return;
        }

        InputBox.Clear();
        SendBtn.IsEnabled   = false;
        LoadingOverlay.Visibility = Visibility.Visible;
        EmptyHint.Visibility      = Visibility.Collapsed;

        var userMsg = new AiChatMessage { Content = text, IsUser = true };
        _history.Add(userMsg);
        AppendMessage(userMsg);

        var result = await _ai.ChatAsync(_history, SchemaHintBox.Text, CurrentDatabase);

        if (result.Error != null)
        {
            var errMsg = new AiChatMessage
            {
                Content = $"❌ {result.Error}", IsUser = false, IsError = true
            };
            _history.Add(errMsg);
            AppendMessage(errMsg);
        }
        else
        {
            var aiMsg = AiChatMessage.FromAssistant(result.RawResponse ?? "");
            _history.Add(aiMsg);
            AppendMessage(aiMsg);
        }

        SendBtn.IsEnabled         = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ChatScroller.ScrollToBottom();
    }

    // ── 訊息氣泡 ─────────────────────────────────────────────

    private void AppendMessage(AiChatMessage msg)
    {
        var bubble = BuildBubble(msg);
        ChatPanel.Children.Add(bubble);
        ChatScroller.ScrollToBottom();
    }

    private Border BuildBubble(AiChatMessage msg)
    {
        var isUser = msg.IsUser;
        var outer  = new Border
        {
            Margin              = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth            = 580
        };

        var inner = new StackPanel();

        // 發送者標籤
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        labelRow.Children.Add(new TextBlock
        {
            Text       = isUser ? "你" : "🤖 AI",
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isUser
                ? new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))
                : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        });
        labelRow.Children.Add(new TextBlock
        {
            Text       = msg.Time.ToString("HH:mm"),
            FontSize   = 10,
            Margin     = new Thickness(8, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
            VerticalAlignment = VerticalAlignment.Bottom
        });
        inner.Children.Add(labelRow);

        // 訊息泡泡
        var bubbleBg = isUser
            ? Color.FromRgb(0x19, 0x76, 0xD2)
            : (msg.IsError ? Color.FromRgb(0xFD, 0xED, 0xED) : Color.FromRgb(0xFF, 0xFF, 0xFF));

        var bubble = new Border
        {
            Background      = new SolidColorBrush(bubbleBg),
            CornerRadius    = new CornerRadius(isUser ? 12 : 4, 12, 12, isUser ? 4 : 12),
            Padding         = new Thickness(14, 10, 14, 10),
            BorderThickness = isUser ? new Thickness(0) : new Thickness(1),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0))
        };

        if (msg.IsUser)
        {
            bubble.Child = new TextBlock
            {
                Text         = msg.Content,
                Foreground   = Brushes.White,
                FontSize     = 13,
                TextWrapping = TextWrapping.Wrap
            };
        }
        else
        {
            // AI 回覆：解析 SQL block
            var contentPanel = new StackPanel();
            var parts = ParseContent(msg.Content);
            foreach (var (text, isSql) in parts)
            {
                if (isSql)
                    contentPanel.Children.Add(BuildSqlBlock(text));
                else if (!string.IsNullOrWhiteSpace(text))
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text         = text.Trim(),
                        FontSize     = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = msg.IsError
                            ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))
                            : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                        Margin       = new Thickness(0, 0, 0, 4)
                    });
            }
            bubble.Child = contentPanel;
        }

        inner.Children.Add(bubble);
        outer.Child = inner;
        return outer;
    }

    private Border BuildSqlBlock(string sql)
    {
        var block = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(12, 10, 12, 10),
            Margin          = new Thickness(0, 6, 0, 6)
        };

        var panel = new DockPanel();

        // SQL 文字
        var tb = new TextBlock
        {
            Text         = sql,
            FontFamily   = new FontFamily("Consolas"),
            FontSize     = 12,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xA8, 0xCC, 0x8C)),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(tb, Dock.Top);
        panel.Children.Add(tb);

        // 按鈕列
        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 8, 0, 0)
        };
        var copyBtn  = MakeCodeButton("📋 複製", () => Clipboard.SetText(sql));
        var insertBtn= MakeCodeButton("▶ 插入編輯器", () => { InsertSqlRequested?.Invoke(sql); });
        btns.Children.Add(copyBtn);
        btns.Children.Add(insertBtn);
        DockPanel.SetDock(btns, Dock.Bottom);
        panel.Children.Add(btns);

        block.Child = panel;
        return block;
    }

    private static Button MakeCodeButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content         = text,
            FontSize        = 11,
            Padding         = new Thickness(10, 4, 10, 4),
            Margin          = new Thickness(0, 0, 6, 0),
            Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // 解析 markdown 中的 ```sql ... ``` 區塊
    private static List<(string text, bool isSql)> ParseContent(string content)
    {
        var parts  = new List<(string, bool)>();
        var regex  = new System.Text.RegularExpressions.Regex(
            @"```(?:sql)?\s*([\s\S]+?)```",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        int last = 0;
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(content))
        {
            if (m.Index > last)
                parts.Add((content[last..m.Index], false));
            parts.Add((m.Groups[1].Value.Trim(), true));
            last = m.Index + m.Length;
        }
        if (last < content.Length)
            parts.Add((content[last..], false));
        return parts;
    }

    // ── 工具列按鈕 ───────────────────────────────────────────

    private void ClearChat_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        ChatPanel.Children.Clear();
        EmptyHint.Visibility = Visibility.Visible;
    }

    private void ApiKey_Click(object sender, RoutedEventArgs e)
        => new AiSettingsDialog { Owner = this }.ShowDialog();

    private void QuickPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string prompt)
        {
            InputBox.Text = prompt;
            InputBox.Focus();
            InputBox.CaretIndex = prompt.Length;
        }
    }
}
