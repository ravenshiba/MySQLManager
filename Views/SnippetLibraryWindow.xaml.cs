using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class SnippetLibraryWindow : Window
{
    private readonly SnippetService _svc = App.SnippetService;
    private Snippet? _current;
    private bool _isNew;

    // 外部可訂閱：使用者選擇插入某段 SQL
    public event Action<string>? InsertRequested;

    public SnippetLibraryWindow() { InitializeComponent(); Loaded += (_, _) => App.FitWindowToScreen(this); }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshCategories();
        RefreshList();
    }

    // ── 清單 ──────────────────────────────────────────────────

    private void RefreshList()
    {
        var kw  = SearchBox.Text;
        var cat = (CategoryBox.SelectedItem as string) == "全部" ? null
                : (CategoryBox.SelectedItem as string);
        var items = _svc.Search(kw, cat);
        SnippetList.ItemsSource = items;
        TotalLabel.Text = $"{items.Count} 個片段";
    }

    private void RefreshCategories()
    {
        var cats = _svc.GetCategories();
        CategoryBox.ItemsSource = cats;
        CategoryBox.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshList();

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is Snippet s)
            LoadSnippet(s, isNew: false);
    }

    // ── 編輯表單 ──────────────────────────────────────────────

    private void LoadSnippet(Snippet s, bool isNew)
    {
        _current = s;
        _isNew   = isNew;
        TitleBox.Text   = s.Title;
        SqlBox.Text     = s.Sql;
        TagsBox.Text    = s.Tags;

        // 設定分類下拉
        var cats = _svc.GetCategories().Where(c => c != "全部").ToList();
        if (!cats.Contains(s.Category)) cats.Add(s.Category);
        CategoryBox.ItemsSource   = cats;
        CategoryBox.SelectedItem  = s.Category;
        if (CategoryBox.SelectedItem == null) CategoryBox.Text = s.Category;

        EmptyPanel.Visibility = Visibility.Collapsed;
        EditPanel.Visibility  = Visibility.Visible;
    }

    private void AddSnippet_Click(object sender, RoutedEventArgs e)
        => LoadSnippet(new Snippet(), isNew: true);

    private void SaveSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show("請填寫標題。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _current.Title    = TitleBox.Text.Trim();
        _current.Sql      = SqlBox.Text;
        _current.Category = CategoryBox.Text?.Trim() ?? "一般";
        _current.Tags     = TagsBox.Text.Trim();

        if (_isNew) { _svc.Add(_current.Title, _current.Sql, _current.Category, _current.Tags); _isNew = false; }
        else        _svc.Update(_current);

        RefreshCategories();
        RefreshList();
    }

    private void DeleteSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MessageBox.Show($"確定刪除「{_current.Title}」？", "確認刪除",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _svc.Delete(_current.Id);
        _current = null;
        EditPanel.Visibility  = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        RefreshCategories();
        RefreshList();
    }

    private void InsertSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        _svc.IncrementUse(_current.Id);
        InsertRequested?.Invoke(SqlBox.Text);
        RefreshList();
        Close();
    }
}
