using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class AuditLogWindow : Window
{
    public AuditLogWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        var keyword = SearchBox.Text.Trim();
        var type    = (TypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var entries = App.AuditLogService.Filter(
            string.IsNullOrEmpty(keyword) ? null : keyword,
            type == "全部" ? null : type);

        LogGrid.ItemsSource = entries;

        // Stats
        var total   = entries.Count;
        var success = entries.Count(e => e.Success);
        var avgMs   = entries.Any() ? (long)entries.Average(e => e.ElapsedMs) : 0;
        StatsText.Text = $"共 {total} 筆記錄  ✅ {success} 成功  ❌ {total - success} 失敗  平均耗時 {avgMs} ms";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void TypeFilter_Changed(object sender, SelectionChangedEventArgs e)  => Refresh();
    private void Refresh_Click(object sender, RoutedEventArgs e)  => Refresh();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("確定清除所有 Audit Log？", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            App.AuditLogService.Clear();
            Refresh();
        }
    }

    private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogGrid.SelectedItem is AuditLogEntry entry)
            SqlFullText.Text = entry.Sql;
    }

    private void CopySql_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SqlFullText.Text))
            Clipboard.SetText(SqlFullText.Text);
    }
}
