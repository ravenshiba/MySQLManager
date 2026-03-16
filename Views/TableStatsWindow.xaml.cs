using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MySQLManager.Models;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class TableStatsWindow : Window
{
    private List<TableStatEntry> _allEntries = new();

    public TableStatsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var svc = GetConn();
        if (svc?.IsConnected != true) { Close(); return; }

        var dbs = await svc.GetDatabasesAsync();
        DbCombo.ItemsSource = dbs;
        // System databases last
        var userDb = dbs.Where(d => !IsSystemDb(d)).ToList();
        if (userDb.Any()) DbCombo.SelectedItem = userDb.First();
        else if (dbs.Any()) DbCombo.SelectedIndex = 0;
    }

    private async void DbCombo_Changed(object sender, SelectionChangedEventArgs e)
        => await LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var db  = DbCombo.SelectedItem?.ToString();
        var svc = GetConn();
        if (db == null || svc == null) return;

        StatusBar.Text = "載入中...";
        _allEntries    = await svc.GetTableStatsAsync(db);
        ApplyFilter();
        BuildSummaryCards(db);

        long   totalRows = _allEntries.Sum(t => t.RowCount);
        double totalMb   = _allEntries.Sum(t => t.TotalMb);
        StatusBar.Text   = $"{db}  |  {_allEntries.Count} 張資料表  |  {totalRows:N0} 行  |  {FormatMb(totalMb)} 總大小";
    }

    private void ApplyFilter()
    {
        var kw = SearchBox.Text.Trim().ToLowerInvariant();
        StatsGrid.ItemsSource = string.IsNullOrEmpty(kw)
            ? _allEntries
            : _allEntries.Where(t => t.TableName.ToLowerInvariant().Contains(kw)).ToList();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void BuildSummaryCards(string database)
    {
        SummaryCards.Children.Clear();
        if (!_allEntries.Any()) return;

        var largest  = _allEntries.MaxBy(t => t.TotalMb);
        var mostRows = _allEntries.MaxBy(t => t.RowCount);
        var newest   = _allEntries.Where(t => t.UpdateTime.HasValue).MaxBy(t => t.UpdateTime);
        var noUpdate = _allEntries.Count(t => !t.UpdateTime.HasValue);
        var totalMb  = _allEntries.Sum(t => t.TotalMb);
        var engines  = _allEntries.GroupBy(t => t.Engine)
                                  .Select(g => $"{g.Key}({g.Count()})")
                                  .Take(3);

        void AddCard(string icon, string title, string value, string sub, string color)
        {
            var card = new Border
            {
                Width = 185, Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding      = new Thickness(14, 10, 14, 10),
                Margin       = new Thickness(0, 0, 10, 0)
            };
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 1, Opacity = 0.08 };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{icon}  {title}", FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9AA0A6")),
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new TextBlock
            {
                Text = value, FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            });
            sp.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9AA0A6")),
                Margin = new Thickness(0, 2, 0, 0)
            });
            card.Child = sp;
            SummaryCards.Children.Add(card);
        }

        AddCard("📦", "總資料量", $"{_allEntries.Count} 張表", $"總計 {FormatMb(totalMb)}", "#1976D2");
        if (largest  != null) AddCard("💾", "最大資料表", largest.TableName,  $"{largest.TotalMbLabel}  |  {largest.RowCountLabel} 行", "#E65100");
        if (mostRows != null) AddCard("📈", "最多行數",   mostRows.TableName, $"{mostRows.RowCountLabel} 行", "#1E8E3E");
        if (newest   != null) AddCard("🕐", "最近更新",   newest.TableName,   newest.UpdateLabel, "#7C4DFF");
        AddCard("⚙️", "儲存引擎", string.Join(", ", engines), $"{noUpdate} 張從未更新", "#78909C");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!_allEntries.Any()) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"table_stats_{DateTime.Now:yyyyMMdd}.csv",
            Filter   = "CSV (*.csv)|*.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("資料表,ENGINE,估計行數,資料大小(MB),索引大小(MB),總大小,最後更新,建立日期");
        foreach (var t in _allEntries)
            sb.AppendLine($"{t.TableName},{t.Engine},{t.RowCount},{t.DataMb:F3},{t.IndexMb:F3},{t.TotalMb:F3},{t.UpdateLabel},{t.CreateLabel}");
        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
    }

    private static string FormatMb(double mb) =>
        mb >= 1024 ? $"{mb/1024:F2} GB" : $"{mb:F1} MB";

    private static bool IsSystemDb(string db) =>
        db is "information_schema" or "mysql" or "performance_schema" or "sys";

    private static ConnectionService? GetConn()
    {
        var vm = Application.Current.MainWindow?.DataContext as MainViewModel;
        return vm?.ActiveSession?.ConnectionService;
    }
}
