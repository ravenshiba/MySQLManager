using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MySQLManager.Models;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class SlowQueryWindow : Window
{
    private List<SlowQueryEntry> _entries = new();

    public SlowQueryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        Loaded += OnLoaded;
        SizeChanged += (_, _) => DrawChart();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await AnalyzeAsync();

    private async void Analyze_Click(object sender, RoutedEventArgs e)
        => await AnalyzeAsync();

    private async System.Threading.Tasks.Task AnalyzeAsync()
    {
        var svc = GetConn();
        if (svc?.IsConnected != true)
        {
            StatusText.Text = "❌ 未連線";
            return;
        }

        LoadingPanel.Visibility = Visibility.Visible;
        StatusText.Text = "分析中...";

        var threshold = double.TryParse(
            (ThresholdCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var t) ? t : 1.0;
        var sort = (SortCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "平均耗時";

        _entries = await svc.GetSlowQueriesAsync(threshold, sort);

        SlowGrid.ItemsSource = _entries;
        DrawChart();

        var total    = _entries.Count;
        var noIndex  = _entries.Sum(e => e.NoIndexCount);
        var avgMs    = total > 0 ? _entries.Average(e => e.AvgTimeMs) : 0;
        SummaryText.Text = $"共 {total} 條慢查詢  |  無索引次數合計 {noIndex:N0}  |  平均耗時 {avgMs:F0}ms  |  閾值 ≥ {threshold}s";
        StatusText.Text  = $"✅ 已分析 | {total} 條慢查詢";

        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var svc = GetConn();
        if (svc == null) return;
        if (MessageBox.Show("重置 performance_schema 統計計數器？",
            "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await svc.ResetStatementStatsAsync();
        StatusText.Text = "✅ 統計已重置";
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) _ = AnalyzeAsync();
    }

    private void SlowGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SqlDetail.Text = SlowGrid.SelectedItem is SlowQueryEntry entry ? entry.DigestSql : "";
    }

    private void CopySql_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SqlDetail.Text))
            Clipboard.SetText(SqlDetail.Text);
    }

    private void Explain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SqlDetail.Text)) return;
        var win = new ExplainWindow(SqlDetail.Text) { Owner = this };
        win.Show();
    }

    // ── 水平長條圖 ────────────────────────────────────────────

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        if (_entries.Count == 0) return;

        double w      = Math.Max(ChartCanvas.ActualWidth,  800);
        double h      = Math.Max(ChartCanvas.ActualHeight, 180);
        double padL   = 180, padR = 80, padT = 20, padB = 20;
        double chartW = w - padL - padR;

        var top = _entries.Take(10).ToList();
        if (!top.Any()) return;

        double maxMs  = top.Max(e => e.AvgTimeMs);
        double rowH   = (h - padT - padB) / top.Count;
        double barH   = Math.Min(rowH * 0.65, 32);

        for (int i = 0; i < top.Count; i++)
        {
            var entry  = top[i];
            double y   = padT + i * rowH + (rowH - barH) / 2;
            double barW = maxMs > 0 ? chartW * entry.AvgTimeMs / maxMs : 0;

            // 背景軌道
            var track = new Rectangle
            {
                Width = chartW, Height = barH,
                Fill  = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)),
                RadiusX = 4, RadiusY = 4
            };
            Canvas.SetLeft(track, padL);
            Canvas.SetTop(track, y);
            ChartCanvas.Children.Add(track);

            // 值條
            if (barW > 1)
            {
                var fillColor = entry.AvgTimeMs switch
                {
                    >= 10000 => Color.FromRgb(0xEF, 0x53, 0x50),
                    >= 3000  => Color.FromRgb(0xFF, 0x70, 0x43),
                    >= 1000  => Color.FromRgb(0xFF, 0xA7, 0x26),
                    _        => Color.FromRgb(0x19, 0x76, 0xD2)
                };
                var bar = new Rectangle
                {
                    Width   = Math.Max(barW, 4), Height = barH,
                    Fill    = new LinearGradientBrush(
                                fillColor,
                                Color.FromRgb(
                                    (byte)Math.Min(fillColor.R + 30, 255),
                                    (byte)Math.Min(fillColor.G + 30, 255),
                                    (byte)Math.Min(fillColor.B + 30, 255)), 0),
                    RadiusX = 4, RadiusY = 4
                };
                Canvas.SetLeft(bar, padL);
                Canvas.SetTop(bar, y);
                ChartCanvas.Children.Add(bar);
            }

            // 表名 label (左)
            var nameLbl = new TextBlock
            {
                Text         = entry.TableName(entry.Schema, entry.DigestSql, 26),
                FontSize     = 11,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth     = padL - 8,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(nameLbl, 4);
            Canvas.SetTop(nameLbl, y + (barH - 14) / 2);
            ChartCanvas.Children.Add(nameLbl);

            // 值 label (右側)
            var valLbl = new TextBlock
            {
                Text     = entry.AvgTimeLabel,
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68))
            };
            Canvas.SetLeft(valLbl, padL + barW + 6);
            Canvas.SetTop(valLbl, y + (barH - 14) / 2);
            ChartCanvas.Children.Add(valLbl);
        }
    }

    private static ConnectionService? GetConn()
    {
        var vm = Application.Current.MainWindow?.DataContext as MainViewModel;
        return vm?.ActiveSession?.ConnectionService;
    }
}

// Extension helper for chart label
internal static class SlowQueryExt
{
    public static string TableName(this SlowQueryEntry e, string schema, string sql, int maxLen)
    {
        // Extract first table name from SQL digest
        var s    = sql.ToUpperInvariant();
        var idx  = s.IndexOf("FROM ", StringComparison.Ordinal);
        string tbl;
        if (idx >= 0)
        {
            var after = sql[(idx + 5)..].TrimStart().Split(' ', '\n', '\t')[0]
                           .Trim('`', '\"', '\'', ';');
            tbl = string.IsNullOrWhiteSpace(after) ? sql : after;
        }
        else tbl = sql;

        var label = string.IsNullOrEmpty(schema) ? tbl : $"{schema}.{tbl}";
        return label.Length > maxLen ? label[..maxLen] + "…" : label;
    }
}
