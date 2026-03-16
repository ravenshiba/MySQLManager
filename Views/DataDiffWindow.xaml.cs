using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class DataDiffWindow : Window
{
    private DataDiffViewModel Vm => (DataDiffViewModel)DataContext;
    private static readonly SolidColorBrush ChangedCellBg =
        new(Color.FromRgb(0xFF, 0xCC, 0x02));   // amber highlight

    public DataDiffViewModel DiffVm { get; }

    public DataDiffWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        var mainVm = Application.Current.MainWindow?.DataContext
                     as MySQLManager.ViewModels.MainViewModel;
        var conn   = mainVm?.ActiveSession?.ConnectionService
                     ?? MySQLManager.App.ConnectionService;
        DiffVm     = new DataDiffViewModel(conn);
        DataContext = DiffVm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 監聽 DisplayRows 變化 → 重繪表格
        Vm.DisplayRows.CollectionChanged += DisplayRows_CollectionChanged;
        Vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Vm.Columns))
                Dispatcher.Invoke(RebuildTable);
        };

        await Vm.LoadDatabasesAsync();
    }

    // ── 動態表格建構 ──────────────────────────────────────────

    /// <summary>
    /// 每次比對結果出來後，用新欄位清單重建整個表格結構。
    /// 我們把它畫在一個 DataGrid-like Grid，由 code-behind 動態填充。
    /// </summary>
    private void RebuildTable()
    {
        DiffContainer.Children.Clear();
        if (Vm.Columns.Count == 0) return;

        // ── 標題列 ──
        DiffContainer.Children.Add(BuildHeaderRow(Vm.Columns));

        // ── 資料列 ──
        foreach (var row in Vm.DisplayRows)
            DiffContainer.Children.Add(BuildDataRow(row, Vm.Columns));

    }


    private void DisplayRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (Vm.Columns.Count == 0)
            {
                RebuildTable();
                return;
            }
            // 重置就重建（filter toggle 等場景）
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                RebuildTable();
                return;
            }
            if (e.NewItems != null)
                foreach (DiffRow row in e.NewItems)
                    DiffContainer.Children.Add(BuildDataRow(row, Vm.Columns));
        });
    }

    // ── 標題列 ────────────────────────────────────────────────

    private static Grid BuildHeaderRow(List<string> cols)
    {
        var g = new Grid { Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)) };
        SetupGridCols(g, cols);

        // 狀態欄標題
        g.Children.Add(MakeHeaderCell("", 0, Brushes.Transparent));

        // 左表標題群組  (col 1)
        var leftGroup = new Border
        {
            Background    = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD)),
            BorderBrush   = new SolidColorBrush(Color.FromRgb(0xBB, 0xDE, 0xFB)),
            BorderThickness = new Thickness(0, 0, 2, 1),
            Padding       = new Thickness(8, 6, 8, 6)
        };
        Grid.SetColumn(leftGroup, 1);
        Grid.SetColumnSpan(leftGroup, cols.Count);
        leftGroup.Child = new TextBlock
        {
            Text       = "← 左側（基準）",
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))
        };
        g.Children.Add(leftGroup);

        // 右表標題群組  (col 1+N+1)
        var rightGroup = new Border
        {
            Background    = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)),
            BorderBrush   = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding       = new Thickness(8, 6, 8, 6)
        };
        Grid.SetColumn(rightGroup, 1 + cols.Count + 1);
        Grid.SetColumnSpan(rightGroup, cols.Count);
        rightGroup.Child = new TextBlock
        {
            Text       = "右側（對比）→",
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
        };
        g.Children.Add(rightGroup);

        return g;
    }

    private static Border MakeHeaderCell(string text, int col,
        Brush? bg = null, bool isSep = false)
    {
        var b = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background      = bg ?? new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
            Padding         = new Thickness(8, 5, 8, 5)
        };
        Grid.SetColumn(b, col);
        b.Child = new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C))
        };
        return b;
    }

    // ── 資料列 ────────────────────────────────────────────────

    private Border BuildDataRow(DiffRow row, List<string> cols)
    {
        var bg = ParseColor(row.KindColor);
        var rowBorder = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background      = bg
        };

        var g = new Grid();
        SetupGridCols(g, cols);

        // ── 種類徽章欄 (col 0) ──
        var badge = BuildKindBadge(row);
        Grid.SetColumn(badge, 0);
        g.Children.Add(badge);

        // ── 左表資料 (cols 1..N) ──
        for (int i = 0; i < cols.Count; i++)
        {
            var col  = cols[i];
            var val  = row.LeftValues.TryGetValue(col, out var v) ? v?.ToString() ?? "NULL" : "—";
            var isChanged = row.ChangedCols.Contains(col);
            var cell = BuildCell(val, isChanged && row.Kind == DiffRowKind.Modified, false, col);
            Grid.SetColumn(cell, 1 + i);
            g.Children.Add(cell);
        }

        // ── 分隔線 (col N+1) ──
        var sep = new Border
        {
            Width      = 3,
            Background = row.Kind == DiffRowKind.Unchanged
                ? new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
                : new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD))
        };
        Grid.SetColumn(sep, 1 + cols.Count);
        g.Children.Add(sep);

        // ── 右表資料 (cols N+2..2N+1) ──
        for (int i = 0; i < cols.Count; i++)
        {
            var col  = cols[i];
            var val  = row.RightValues.TryGetValue(col, out var v) ? v?.ToString() ?? "NULL" : "—";
            var isChanged = row.ChangedCols.Contains(col);
            var cell = BuildCell(val, isChanged && row.Kind == DiffRowKind.Modified, true, col);
            Grid.SetColumn(cell, 1 + cols.Count + 1 + i);
            g.Children.Add(cell);
        }

        rowBorder.Child = g;
        return rowBorder;
    }

    private static void SetupGridCols(Grid g, List<string> cols)
    {
        g.ColumnDefinitions.Clear();
        // col 0: 種類徽章
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        // 左表欄位
        for (int i = 0; i < cols.Count; i++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // 分隔
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        // 右表欄位
        for (int i = 0; i < cols.Count; i++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private Border BuildCell(string text, bool isChanged, bool isRight, string colName)
    {
        Brush cellBg = isChanged
            ? new SolidColorBrush(isRight
                ? Color.FromArgb(0xCC, 0xFF, 0xEE, 0x88)   // 右：amber
                : Color.FromArgb(0x99, 0xFF, 0xCC, 0xCC))   // 左：red tint（刪除的舊值）
            : Brushes.Transparent;

        var border = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background      = cellBg,
            Padding         = new Thickness(8, 5, 8, 5),
            MinWidth        = 80,
            MaxWidth        = 300
        };

        var tb = new TextBlock
        {
            Text          = text,
            FontSize      = 12,
            FontFamily    = new FontFamily("Consolas"),
            TextTrimming  = TextTrimming.CharacterEllipsis,
            ToolTip       = text.Length > 40 ? text : null,
            Foreground    = text == "NULL" || text == "—"
                ? new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD))
                : new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21))
        };

        if (isChanged)
        {
            tb.FontWeight = FontWeights.SemiBold;
            // 欄位名稱小標籤
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text       = colName,
                FontSize   = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0x75, 0x00)),
                Margin     = new Thickness(0, 0, 0, 1)
            });
            panel.Children.Add(tb);
            border.Child = panel;
        }
        else
        {
            border.Child = tb;
        }

        return border;
    }

    private Border BuildKindBadge(DiffRow row)
    {
        var b = new Border
        {
            Padding             = new Thickness(6, 0, 6, 0),
            BorderBrush         = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)),
            BorderThickness     = new Thickness(0, 0, 1, 0),
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        if (row.Kind != DiffRowKind.Unchanged)
        {
            var badge = new Border
            {
                Background        = ParseColor(row.KindBadgeColor),
                CornerRadius      = new CornerRadius(3),
                Padding           = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text       = row.KindLabel,
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            b.Child = badge;
        }
        return b;
    }

    private static SolidColorBrush ParseColor(string hex)
    {
        try
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        }
        catch { return new SolidColorBrush(Colors.Transparent); }
    }

    // ── 事件處理 ──────────────────────────────────────────────

    private void ToggleUnchanged_Click(object sender, RoutedEventArgs e)
    {
        Vm.ShowUnchanged = !Vm.ShowUnchanged;
    }
}
