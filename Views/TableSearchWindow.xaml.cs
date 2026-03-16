using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MySQLManager.Helpers;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

// ── 搜尋條件模型 ──────────────────────────────────────────────

public class SearchCondition : BaseViewModel
{
    private string _columnName = "";
    public  string ColumnName { get => _columnName; set => SetProperty(ref _columnName, value); }

    private string _operator = "=";
    public  string Operator  { get => _operator;  set => SetProperty(ref _operator,  value); }

    private string _value = "";
    public  string Value     { get => _value;     set => SetProperty(ref _value,     value); }

    private string _sortOrder = "—";
    public  string SortOrder  { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }

    private string _logicOp = "AND";
    public  string LogicOp   { get => _logicOp;   set => SetProperty(ref _logicOp,   value); }
}

// ── ViewModel ─────────────────────────────────────────────────

public class TableSearchViewModel : BaseViewModel
{
    private readonly ConnectionService _conn;

    // ─ 選擇 ───────────────────────────────────────────────────
    public ObservableCollection<string> Databases { get; } = new();
    public ObservableCollection<string> Tables    { get; } = new();

    private string? _selectedDatabase;
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set { SetProperty(ref _selectedDatabase, value); _ = LoadTablesAsync(); }
    }

    private string? _selectedTable;
    public string? SelectedTable
    {
        get => _selectedTable;
        set { SetProperty(ref _selectedTable, value); HasColumns = false; Conditions.Clear(); }
    }

    // ─ 欄位 / 條件 ────────────────────────────────────────────
    public ObservableCollection<string>          ColumnNames { get; } = new();
    public ObservableCollection<SearchCondition> Conditions  { get; } = new();

    public List<string> Operators  { get; } = new() { "=", "!=", ">", "<", ">=", "<=", "LIKE", "NOT LIKE", "IS NULL", "IS NOT NULL", "IN" };
    public List<string> SortOrders { get; } = new() { "—", "ASC", "DESC" };
    public List<string> LogicOps   { get; } = new() { "AND", "OR" };
    public List<int>    PageSizes  { get; } = new() { 50, 100, 200, 500 };

    private int _selectedPageSize = 100;
    public int SelectedPageSize { get => _selectedPageSize; set => SetProperty(ref _selectedPageSize, value); }

    private bool _hasColumns;
    public bool HasColumns { get => _hasColumns; set => SetProperty(ref _hasColumns, value); }

    // ─ 結果 ───────────────────────────────────────────────────
    private DataView? _resultData;
    public DataView? ResultData { get => _resultData; set => SetProperty(ref _resultData, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; set => SetProperty(ref _hasResult, value); }

    private int _currentPage = 1;
    private int _totalRows;

    public string PageLabel    => $"第 {_currentPage} 頁  /  共 {(int)Math.Ceiling(_totalRows / (double)SelectedPageSize)} 頁  ({_totalRows:N0} 筆)";
    public string GeneratedSql => BuildSql(false, _currentPage);

    private string _statusText = "選擇資料庫與資料表後按「載入欄位」";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public TableSearchViewModel(ConnectionService conn)
    {
        _conn = conn;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var dbs = await _conn.GetDatabasesAsync();
        foreach (var db in dbs) Databases.Add(db);
        SelectedDatabase = Databases.FirstOrDefault();
    }

    private async Task LoadTablesAsync()
    {
        Tables.Clear();
        if (SelectedDatabase == null) return;
        var tables = await _conn.GetTablesAsync(SelectedDatabase);
        foreach (var t in tables) Tables.Add(t);
        SelectedTable = Tables.FirstOrDefault();
    }

    public async Task LoadColumnsAsync()
    {
        if (SelectedDatabase == null || SelectedTable == null) return;
        ColumnNames.Clear();
        Conditions.Clear();
        var cols = await _conn.GetColumnsAsync(SelectedDatabase, SelectedTable);
        foreach (var c in cols) ColumnNames.Add(c.Field);

        // 預設加一個空條件
        AddCondition();
        HasColumns = true;
        StatusText = $"已載入 {cols.Count} 個欄位，設定搜尋條件後按「搜尋」";
        OnPropertyChanged(nameof(GeneratedSql));
    }

    public void AddCondition()
    {
        var cond = new SearchCondition
        {
            ColumnName = ColumnNames.FirstOrDefault() ?? "",
            Operator   = "=",
            SortOrder  = "—",
            LogicOp    = "AND"
        };
        cond.PropertyChanged += (_, _) => OnPropertyChanged(nameof(GeneratedSql));
        Conditions.Add(cond);
    }

    public void RemoveCondition(SearchCondition c) => Conditions.Remove(c);

    public void Clear()
    {
        Conditions.Clear();
        AddCondition();
        ResultData = null;
        HasResult  = false;
        _totalRows = 0;
        _currentPage = 1;
        StatusText = "條件已清除";
        OnPropertyChanged(nameof(GeneratedSql));
        OnPropertyChanged(nameof(PageLabel));
    }

    public async Task SearchAsync(int page = 1)
    {
        if (SelectedDatabase == null || SelectedTable == null) return;
        _currentPage = page;

        // 先取總筆數
        var countSql = BuildSql(true, 1);
        var cr = await _conn.ExecuteQueryAsync(countSql, SelectedDatabase);
        if (cr.Success && cr.Data?.Rows.Count > 0)
            _totalRows = Convert.ToInt32(cr.Data.Rows[0][0]);

        // 取分頁資料
        var sql = BuildSql(false, page);
        var r = await _conn.ExecuteQueryAsync(sql, SelectedDatabase);
        if (r.Success)
        {
            ResultData = r.Data?.DefaultView;
            HasResult  = true;
            StatusText = $"✅ 共 {_totalRows:N0} 筆  |  耗時 {r.ExecutionTimeMs:F0} ms";
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(GeneratedSql));
        }
        else
        {
            StatusText = $"❌ {r.ErrorMessage}";
        }
    }

    // ─ SQL 建立 ────────────────────────────────────────────────
    private string BuildSql(bool countOnly, int page)
    {
        if (SelectedTable == null) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(countOnly
            ? $"SELECT COUNT(*) FROM `{SelectedDatabase}`.`{SelectedTable}`"
            : $"SELECT * FROM `{SelectedDatabase}`.`{SelectedTable}`");

        var active = Conditions.Where(c =>
            !string.IsNullOrEmpty(c.ColumnName) &&
            c.Operator != "IS NULL" && c.Operator != "IS NOT NULL"
                ? !string.IsNullOrEmpty(c.Value)
                : true).ToList();

        if (active.Any())
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (i > 0) sb.Append($" {c.LogicOp} ");

                var val = c.Value.Replace("'", "''");
                sb.Append(c.Operator switch
                {
                    "IS NULL"     => $"`{c.ColumnName}` IS NULL",
                    "IS NOT NULL" => $"`{c.ColumnName}` IS NOT NULL",
                    "LIKE"        => $"`{c.ColumnName}` LIKE '%{val}%'",
                    "NOT LIKE"    => $"`{c.ColumnName}` NOT LIKE '%{val}%'",
                    "IN"          => $"`{c.ColumnName}` IN ({string.Join(",", val.Split(',').Select(v => $"'{v.Trim()}'"))})",
                    _             => $"`{c.ColumnName}` {c.Operator} '{val}'"
                });
            }
        }

        // ORDER BY
        var sortCols = Conditions
            .Where(c => !string.IsNullOrEmpty(c.ColumnName) && c.SortOrder != "—")
            .Select(c => $"`{c.ColumnName}` {c.SortOrder}").ToList();
        if (sortCols.Any())
            sb.Append($" ORDER BY {string.Join(", ", sortCols)}");

        // LIMIT / OFFSET
        if (!countOnly)
        {
            var offset = (page - 1) * SelectedPageSize;
            sb.Append($" LIMIT {SelectedPageSize} OFFSET {offset}");
        }

        return sb.ToString();
    }
}

// ── Code-behind ───────────────────────────────────────────────

public partial class TableSearchWindow : Window
{
    private TableSearchViewModel Vm => (TableSearchViewModel)DataContext;

    public TableSearchWindow(string? database = null, string? table = null)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new TableSearchViewModel(GetActiveConn());
        if (database != null)
        {
            Vm.SelectedDatabase = database;
            if (table != null) Vm.SelectedTable = table;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5) { _ = Vm.SearchAsync(); e.Handled = true; }
    }

    private async void LoadColumns_Click(object s, RoutedEventArgs e) => await Vm.LoadColumnsAsync();
    private async void Search_Click(object s, RoutedEventArgs e)      => await Vm.SearchAsync();
    private void AddCondition_Click(object s, RoutedEventArgs e)      => Vm.AddCondition();
    private void Clear_Click(object s, RoutedEventArgs e)             => Vm.Clear();

    private void RemoveCondition_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement fe && fe.Tag is SearchCondition c)
            Vm.RemoveCondition(c);
    }

    private void CopySql_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Vm.GeneratedSql))
            Clipboard.SetText(Vm.GeneratedSql);
    }

    private async void PrevPage_Click(object s, RoutedEventArgs e)
    {
        if (Vm.HasResult) await Vm.SearchAsync(
            Math.Max(1, int.Parse(Vm.PageLabel.Split(' ')[1]) - 1));
    }

    private async void NextPage_Click(object s, RoutedEventArgs e)
    {
        if (Vm.HasResult) await Vm.SearchAsync(
            int.Parse(Vm.PageLabel.Split(' ')[1]) + 1);
    }
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
