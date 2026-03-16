using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class QueryHistoryWindow : Window
{
    private readonly QueryHistoryService    _svc;
    private QueryHistoryEntry?              _selected;
    private string                          _sortMode = "newest";

    public string? ChosenSql      { get; private set; }
    public string? ChosenDatabase { get; private set; }
    public bool    ShouldRun      { get; private set; }

    public QueryHistoryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _svc = App.HistoryService;
        Loaded += (_, _) => { ApplyFilter(); SearchBox.Focus(); };
    }

    // ── 篩選 / 排序 ────────────────────────────────────────────
    private void ApplyFilter()
    {
        var keyword = SearchBox.Text.Trim();
        IEnumerable<QueryHistoryEntry> entries =
            string.IsNullOrEmpty(keyword) ? _svc.Entries : _svc.Search(keyword);

        entries = _sortMode switch
        {
            "slow"     => _svc.SortByDuration(),
            "favorite" => _svc.GetFavorites(),
            _          => entries.OrderByDescending(e => e.ExecutedAt)
        };

        if (!string.IsNullOrEmpty(keyword) && _sortMode == "favorite")
            entries = entries.Where(e => e.Sql.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        HistoryList.ItemsSource = entries.ToList();
        CountLabel.Text = $"共 {HistoryList.Items.Count} 筆";
    }

    private void SearchBox_TextChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter();

    private void SortCombo_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _sortMode = SortCombo.SelectedIndex switch { 1 => "slow", 2 => "favorite", _ => "newest" };
        ApplyFilter();
    }

    // ── 選取 ──────────────────────────────────────────────────
    private void HistoryList_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not QueryHistoryEntry entry)
        {
            EmptyHint.Visibility   = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }
        _selected              = entry;
        EmptyHint.Visibility   = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        DetailTime.Text  = entry.DisplayTime;
        DetailDb.Text    = entry.Database ?? "—";
        DetailInfo.Text  = entry.ExecInfo;
        SqlPreview.Text  = entry.Sql;
        TagBox.Text      = entry.Tags;
    }

    // ── 星號點擊 ──────────────────────────────────────────────
    private void Star_Click(object s, MouseButtonEventArgs e)
    {
        if ((s as FrameworkElement)?.DataContext is QueryHistoryEntry entry)
        {
            _svc.ToggleFavorite(entry.Id);
            ApplyFilter();
            e.Handled = true;
        }
    }

    // ── 儲存標籤 ──────────────────────────────────────────────
    private void SaveTag_Click(object s, RoutedEventArgs e)
    {
        if (_selected == null) return;
        _svc.SetTags(_selected.Id, TagBox.Text.Trim());
        ApplyFilter();
    }

    // ── 操作按鈕 ──────────────────────────────────────────────
    private void OpenInEditor_Click(object s, RoutedEventArgs e)
    {
        if (_selected == null) return;
        ChosenSql = _selected.Sql; ChosenDatabase = _selected.Database;
        ShouldRun = false;
        DialogResult = true;
    }

    private void RunQuery_Click(object s, RoutedEventArgs e)
    {
        if (_selected == null) return;
        ChosenSql = _selected.Sql; ChosenDatabase = _selected.Database;
        ShouldRun = true;
        DialogResult = true;
    }

    private void CopySql_Click(object s, RoutedEventArgs e)
    {
        if (_selected == null) return;
        System.Windows.Clipboard.SetText(_selected.Sql);
        MessageBox.Show("已複製到剪貼簿", "完成", MessageBoxButton.OK, MessageBoxImage.None);
    }

    private void ClearAll_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("確定清除所有歷史記錄？", "確認",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _svc.Clear();
        ApplyFilter();
        EmptyHint.Visibility   = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    // ── 鍵盤快捷鍵 ────────────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) RunQuery_Click(this, new());
            else OpenInEditor_Click(this, new());
        }
        else if (e.Key == Key.Delete && HistoryList.SelectedItem is QueryHistoryEntry del)
        {
            _svc.Remove(del.Id);
            ApplyFilter();
        }
    }
}
