using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MySQLManager.Views;

public partial class ShortcutHelpWindow : Window
{
    private record ShortcutEntry(string Key, string Description, string Group);

    private static readonly List<ShortcutEntry> AllShortcuts = new()
    {
        // 查詢
        new("F5 / Ctrl+Enter",    "執行 SQL",                     "查詢"),
        new("Ctrl+F",             "搜尋 SQL",                     "查詢"),
        new("Ctrl+H",             "取代 SQL",                     "查詢"),
        new("Ctrl+/",             "開啟快捷鍵說明",                "查詢"),
        new("Ctrl+Z",             "復原",                         "查詢"),
        new("Ctrl+Y",             "重做",                         "查詢"),
        new("Ctrl+A",             "全選",                         "查詢"),
        new("Ctrl+Space",         "觸發自動補全",                   "查詢"),
        new("Ctrl+Shift+F",       "格式化 SQL",                   "查詢"),
        new("Ctrl+O",             "開啟 SQL 檔案",                "查詢"),
        new("Ctrl+Shift+O",       "另存 SQL 檔案",                "查詢"),
        new("Ctrl+Shift+S",       "開啟 Snippet 庫",              "查詢"),
        // 分頁
        new("Ctrl+T",             "新增查詢分頁",                   "分頁"),
        new("Ctrl+W",             "關閉目前分頁",                   "分頁"),
        new("Ctrl+Tab",           "切換到下一個分頁",               "分頁"),
        new("Ctrl+1~9",           "切換到第 N 個分頁",              "分頁"),
        // 結果集
        new("Ctrl+C",             "複製選取儲存格",                  "結果集"),
        new("F2",                 "編輯儲存格",                     "結果集"),
        new("Delete",             "清空儲存格值",                   "結果集"),
        new("Ctrl+S",             "儲存變更",                      "結果集"),
        new("Ctrl+E",             "匯出結果為 CSV",                 "結果集"),
        // 視窗
        new("Ctrl+Shift+M",       "開啟伺服器監控",                  "視窗"),
        new("Ctrl+Shift+H",       "開啟查詢歷史",                   "視窗"),
        new("Ctrl+Shift+E",       "開啟 ERD 關聯圖",               "視窗"),
        new("Ctrl+Shift+L",       "開啟 Lock 分析",                "視窗"),
        new("Alt+F4",             "關閉應用程式",                   "視窗"),
        // 連線
        new("Ctrl+N",             "新增連線",                      "連線"),
        new("F9",                 "重新連線",                      "連線"),
        new("Ctrl+Shift+D",       "切斷連線",                      "連線"),
    };

    public ShortcutHelpWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            App.FitWindowToScreen(this);
            RenderGroups(AllShortcuts);
        };
    }

    private void RenderGroups(List<ShortcutEntry> shortcuts)
    {
        GroupPanel.Children.Clear();
        var groups = shortcuts.GroupBy(s => s.Group);

        foreach (var g in groups)
        {
            // Group header
            GroupPanel.Children.Add(new TextBlock
            {
                Text       = g.Key,
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin     = new Thickness(0, 12, 0, 4),
            });

            // Shortcuts in this group
            foreach (var s in g)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var keyBlock = new Border
                {
                    Background   = (Brush)FindResource("SecondaryBrush"),
                    BorderBrush  = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding      = new Thickness(6, 2, 6, 2),
                    Child        = new TextBlock
                    {
                        Text       = s.Key,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize   = 12,
                        Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    }
                };

                var descBlock = new TextBlock
                {
                    Text       = s.Description,
                    FontSize   = 13,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin     = new Thickness(12, 0, 0, 0),
                };

                Grid.SetColumn(keyBlock, 0);
                Grid.SetColumn(descBlock, 1);
                row.Children.Add(keyBlock);
                row.Children.Add(descBlock);
                GroupPanel.Children.Add(row);
            }
        }
    }

    private void Search_Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        var term = SearchBox.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(term)
            ? AllShortcuts
            : AllShortcuts.Where(x =>
                x.Key.ToLower().Contains(term) ||
                x.Description.ToLower().Contains(term) ||
                x.Group.ToLower().Contains(term)).ToList();
        RenderGroups(filtered);
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
