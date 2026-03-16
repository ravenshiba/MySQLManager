using System;
using System.Linq;
using System.Windows;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class IndexSuggestWindow : Window
{
    private readonly string _sql;
    private readonly string _database;
    private readonly AiIndexService _svc;

    public IndexSuggestWindow(string sql, string database)
    {
        InitializeComponent();
        _sql      = sql;
        _database = database;
        _svc      = new AiIndexService(App.ConnectionService);
        Loaded   += async (_, _) =>
        {
            App.FitWindowToScreen(this);
            await RunAnalysisAsync();
        };
    }

    private async System.Threading.Tasks.Task RunAnalysisAsync()
    {
        StatusLabel.Text = "分析中…";
        try
        {
            var suggestions = await _svc.AnalyzeQueryAsync(_sql, _database);
            SuggestionGrid.ItemsSource = suggestions;
            StatusLabel.Text = suggestions.Count == 0
                ? "✅ 未發現明顯的索引問題"
                : $"找到 {suggestions.Count} 個建議";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ {ex.Message}";
        }
    }

    private void SuggestionGrid_SelectionChanged(object s,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SuggestionGrid.SelectedItem is IndexSuggestion suggestion)
            SqlPreview.Text = suggestion.Sql;
    }

    private async void Analyze_Click(object s, RoutedEventArgs e)
        => await RunAnalysisAsync();

    private void CopySql_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SqlPreview.Text))
            Clipboard.SetText(SqlPreview.Text);
    }

    private async void ExecuteIndex_Click(object s, RoutedEventArgs e)
    {
        if (SuggestionGrid.SelectedItem is not IndexSuggestion suggestion) return;
        if (suggestion.Sql.StartsWith("--")) { StatusLabel.Text = "⚠️ 此為建議刪除，請手動確認後執行"; return; }

        var res = MessageBox.Show($"執行以下 SQL？\n\n{suggestion.Sql}",
            "確認建立索引", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        var result = await App.ConnectionService.ExecuteQueryAsync(suggestion.Sql);
        StatusLabel.Text = result.ErrorMessage == null
            ? "✅ 索引已建立成功" : $"❌ {result.ErrorMessage}";

        if (result.ErrorMessage == null)
            await RunAnalysisAsync();
    }
}
