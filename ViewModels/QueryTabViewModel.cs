using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Models;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

public class ResultSnapshot
{
    public string    Label   { get; set; } = "";
    public System.Data.DataTable Data { get; set; } = new();
    public string    ExecutedAt { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    public int       RowCount   => Data.Rows.Count;
}

public class QueryTabViewModel : BaseViewModel
{
    private readonly ConnectionService      _connService;
    private readonly DataEditService        _editService;
    private readonly QueryHistoryService    _historyService;
    private readonly SqlAutoCompleteService _autoComplete;

    // ── 基本屬性 ──────────────────────────────────────────────

    public string TabTitle { get; set; }

    private string _sqlText = "-- 在此輸入 SQL 指令\nSELECT 1;";
    public string SqlText { get => _sqlText; set => SetProperty(ref _sqlText, value); }

    private string? _selectedDatabase;
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set { SetProperty(ref _selectedDatabase, value); }
    }

    // ── 結果 ──────────────────────────────────────────────────

    private DataTable? _resultData;
    public DataTable? ResultData { get => _resultData; set => SetProperty(ref _resultData, value); }

    // ── 結果快照（並排比較用）────────────────────────────────
    public System.Collections.ObjectModel.ObservableCollection<ResultSnapshot> Snapshots { get; }
        = new();

    public void SaveSnapshot(string label)
    {
        if (ResultData == null) return;
        Snapshots.Add(new ResultSnapshot
        {
            Label = label,
            Data  = ResultData.Copy()
        });
        OnPropertyChanged(nameof(HasSnapshots));
    }

    public bool HasSnapshots => Snapshots.Count > 0;

    // ── 分頁 ──────────────────────────────────────────────────
    private DataTable? _fullData;          // 完整資料（原始）
    public  const int  DefaultPageSize = 500;

    private int _pageSize = DefaultPageSize;
    public  int PageSize
    {
        get => _pageSize;
        set { SetProperty(ref _pageSize, Math.Max(1, value)); ApplyPage(); }
    }

    private int _currentPage = 1;
    public  int CurrentPage
    {
        get => _currentPage;
        set
        {
            var clamped = Math.Clamp(value, 1, TotalPages);
            if (SetProperty(ref _currentPage, clamped))
            {
                ApplyPage();
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(RowRangeInfo));
            }
        }
    }

    private int _totalRows;
    public  int TotalRows  { get => _totalRows;  private set => SetProperty(ref _totalRows, value); }
    public  int TotalPages => _totalRows == 0 ? 1 : (int)Math.Ceiling(_totalRows / (double)_pageSize);
    public  bool CanGoPrev => _currentPage > 1;
    public  bool CanGoNext => _currentPage < TotalPages;
    public  bool ShowPager => _totalRows > _pageSize;   // only show bar when needed

    public string PageInfo     => $"{_currentPage} / {TotalPages}";
    public string RowRangeInfo
    {
        get
        {
            if (_totalRows == 0) return "";
            int from = (_currentPage - 1) * _pageSize + 1;
            int to   = Math.Min(_currentPage * _pageSize, _totalRows);
            return $"顯示第 {from:N0}–{to:N0} 筆，共 {_totalRows:N0} 筆";
        }
    }

    public RelayCommand FirstPageCommand { get; private set; } = null!;
    public RelayCommand PrevPageCommand  { get; private set; } = null!;
    public RelayCommand NextPageCommand  { get; private set; } = null!;
    public RelayCommand LastPageCommand  { get; private set; } = null!;

    private void InitPagingCommands()
    {
        FirstPageCommand = new RelayCommand(_ => { CurrentPage = 1; },            _ => CanGoPrev);
        PrevPageCommand  = new RelayCommand(_ => { CurrentPage--; },               _ => CanGoPrev);
        NextPageCommand  = new RelayCommand(_ => { CurrentPage++; },               _ => CanGoNext);
        LastPageCommand  = new RelayCommand(_ => { CurrentPage = TotalPages; },    _ => CanGoNext);
    }

    // 把完整資料切成當頁 DataTable
    private void ApplyPage()
    {
        if (_fullData == null) { ResultData = null; return; }
        if (_totalRows <= _pageSize) { ResultData = _fullData; return; }  // 不需分頁

        var page   = _fullData.Clone();  // 同結構空表
        int start  = (_currentPage - 1) * _pageSize;
        int end    = Math.Min(start + _pageSize, _totalRows);
        for (int i = start; i < end; i++)
            page.ImportRow(_fullData.Rows[i]);
        ResultData = page;

        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageInfo));
        OnPropertyChanged(nameof(RowRangeInfo));
        OnPropertyChanged(nameof(ShowPager));
        // Refresh command CanExecute
        FirstPageCommand.RaiseCanExecuteChanged();
        PrevPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        LastPageCommand.RaiseCanExecuteChanged();
    }

    // 設定全新資料集（查詢完成後呼叫）
    private void SetFullData(DataTable? data)
    {
        _fullData    = data;
        _currentPage = 1;
        _totalRows   = data?.Rows.Count ?? 0;
        OnPropertyChanged(nameof(TotalRows));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(ShowPager));
        OnPropertyChanged(nameof(PageInfo));
        OnPropertyChanged(nameof(RowRangeInfo));
        ApplyPage();
    }

    // 多結果集
    public ObservableCollection<SingleResultSet> MultiResults { get; } = new();

    private bool _hasMultiResults;
    public bool HasMultiResults { get => _hasMultiResults; set => SetProperty(ref _hasMultiResults, value); }

    private SingleResultSet? _activeResult;
    public SingleResultSet? ActiveResult
    {
        get => _activeResult;
        set
        {
            SetProperty(ref _activeResult, value);
            SetFullData(value?.Data);    // 分頁初始化
            // 更新 Tab 高亮狀態
            foreach (var rs in MultiResults)
                rs.IsActive = rs == value;
            // 非 SELECT 結果的說明文字
            NonSelectMessage = (value != null && !value.IsSelect)
                ? $"✅ {value.StatusText}"
                : null;
            OnPropertyChanged(nameof(ShowNonSelectPanel));
        }
    }

    private string? _nonSelectMessage;
    public string? NonSelectMessage
    {
        get => _nonSelectMessage;
        set => SetProperty(ref _nonSelectMessage, value);
    }

    public bool ShowNonSelectPanel =>
        ActiveResult != null && !ActiveResult.IsSelect;

    private string _resultMessage = string.Empty;
    public string ResultMessage { get => _resultMessage; set => SetProperty(ref _resultMessage, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

    // ── AI 輔助 SQL ───────────────────────────────────────────

    private string _aiPrompt = string.Empty;
    public string AiPrompt { get => _aiPrompt; set => SetProperty(ref _aiPrompt, value); }

    private string _aiResult = string.Empty;
    public string AiResult { get => _aiResult; set => SetProperty(ref _aiResult, value); }

    private bool _aiIsLoading;
    public bool AiIsLoading { get => _aiIsLoading; set => SetProperty(ref _aiIsLoading, value); }

    private bool _showAiPanel;
    public bool ShowAiPanel { get => _showAiPanel; set => SetProperty(ref _showAiPanel, value); }

    private bool _showAiSettings;
    public bool ShowAiSettings { get => _showAiSettings; set => SetProperty(ref _showAiSettings, value); }

    // ── 自動完成 ──────────────────────────────────────────────

    private ObservableCollection<CompletionItem> _completions = new();
    public ObservableCollection<CompletionItem> Completions => _completions;

    private bool _showCompletions;
    public bool ShowCompletions
    {
        get => _showCompletions;
        set => SetProperty(ref _showCompletions, value);
    }

    private CompletionItem? _selectedCompletion;
    public CompletionItem? SelectedCompletion
    {
        get => _selectedCompletion;
        set => SetProperty(ref _selectedCompletion, value);
    }

    // ── 編輯模式 ──────────────────────────────────────────────

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set { SetProperty(ref _isEditMode, value); OnPropertyChanged(nameof(EditModeLabel)); OnPropertyChanged(nameof(CanEdit)); }
    }
    public string EditModeLabel => IsEditMode ? "🔒 唯讀" : "✏️ 編輯";
    public bool CanEdit => IsEditMode;

    private string? _editTable;
    private string? _editDatabase;
    private List<string> _primaryKeys = new();
    private readonly HashSet<DataRow> _newRows = new();
    private readonly Dictionary<DataRow, Dictionary<string, object?>> _originalValues = new();
    public bool HasPendingChanges => _newRows.Count > 0 || _originalValues.Count > 0;

    private DataRowView? _selectedRow;
    public DataRowView? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    // ── 命令 ──────────────────────────────────────────────────

    public AsyncRelayCommand  RunQueryCommand       { get; }
    public RelayCommand       ClearResultCommand    { get; }
    public RelayCommand       ToggleEditCommand     { get; }
    public AsyncRelayCommand  SaveChangesCommand    { get; }
    public AsyncRelayCommand  DeleteRowCommand      { get; }
    public RelayCommand       AddRowCommand         { get; }
    public RelayCommand       DiscardChangesCommand { get; }
    public AsyncRelayCommand  ExportCsvCommand      { get; }
    public AsyncRelayCommand  ExportSqlCommand      { get; }
    public RelayCommand       ShowHistoryCommand    { get; }
    public RelayCommand       SelectResultCommand   { get; }
    public RelayCommand       FormatSqlCommand      { get; }
    public AsyncRelayCommand  AiGenerateCommand     { get; }
    public AsyncRelayCommand  AiExplainCommand      { get; }
    public AsyncRelayCommand  ExportXlsxCommand     { get; }

    // 讓 View 可以直接觸發事件
    public event Action<string>? InsertTextRequested;
    public event Action?         OpenHistoryRequested;

    public QueryTabViewModel(string title, ConnectionService connService)
    {
        TabTitle        = title;
        _connService    = connService;
        _editService    = new DataEditService(connService);
        _historyService = App.HistoryService;
        _autoComplete   = App.AutoCompleteService;

        RunQueryCommand       = new AsyncRelayCommand(RunQueryAsync,    () => !IsRunning);
        ClearResultCommand    = new RelayCommand(ClearResult);
        ToggleEditCommand     = new RelayCommand(ToggleEdit);
        SaveChangesCommand    = new AsyncRelayCommand(SaveChangesAsync, () => !IsRunning && HasPendingChanges);
        DeleteRowCommand      = new AsyncRelayCommand(DeleteRowAsync,   () => SelectedRow != null && IsEditMode);
        AddRowCommand         = new RelayCommand(AddRow,                _ => IsEditMode && ResultData != null);
        DiscardChangesCommand = new RelayCommand(DiscardChanges,        _ => HasPendingChanges);
        ExportCsvCommand      = new AsyncRelayCommand(ExportCsvAsync,   () => ResultData != null);
        ExportSqlCommand      = new AsyncRelayCommand(ExportSqlAsync);
        ShowHistoryCommand    = new RelayCommand(_ => OpenHistoryRequested?.Invoke());
        SelectResultCommand   = new RelayCommand(o => { if (o is SingleResultSet rs) ActiveResult = rs; });
        InitPagingCommands();
        FormatSqlCommand      = new RelayCommand(FormatSql);
        AiGenerateCommand     = new AsyncRelayCommand(AiGenerateSqlAsync);
        AiExplainCommand      = new AsyncRelayCommand(AiExplainSqlAsync);
        ExportXlsxCommand     = new AsyncRelayCommand(ExportXlsxAsync, () => ResultData != null);
    }

    // ── 查詢執行 ──────────────────────────────────────────────

    public async Task RunQueryAsync()
    {
        await ExecuteSqlAsync(SqlText);
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        IsRunning = true; HasError = false; ResultData = null;
        ResultMessage = "執行中...";
        IsEditMode = false; _newRows.Clear(); _originalValues.Clear();
        ShowCompletions = false;

        var startTime = DateTime.Now;
        try
        {
            var statements = SplitStatements(sql);
            bool isMulti = statements.Count > 1;

            MultiResults.Clear();
            HasMultiResults = false;
            ActiveResult = null;

            if (isMulti)
            {
                // 多段 SQL：逐段執行，收集所有結果集
                var multi = await _connService.ExecuteMultiQueryAsync(sql, SelectedDatabase);
                if (multi.Success)
                {
                    HasMultiResults = multi.ResultSets.Count >= 1;
                    foreach (var rs in multi.ResultSets)
                        MultiResults.Add(rs);
                    ActiveResult = MultiResults.Count > 0 ? MultiResults[0] : null;
                    if (ActiveResult != null) ActiveResult.IsActive = true;
                    // SetFullData called via ActiveResult setter
                    var totalRows = multi.ResultSets.Where(r => r.IsSelect).Sum(r => r.RowCount);
                    ResultMessage = $"✅ {multi.ResultSets.Count} 個結果集 | 共 {totalRows} 筆 | 耗時 {multi.ExecutionTimeMs:F1} ms";
                    ParseEditableTable(sql);
                    _historyService.Add(new QueryHistoryEntry
                    {
                        Sql = sql.Trim(), Database = SelectedDatabase,
                        Success = true, ExecutionMs = multi.ExecutionTimeMs, RowsAffected = totalRows
                    });
                    App.AuditLogService.Log(_connService.CurrentProfile?.Name ?? "Unknown",
                        SelectedDatabase ?? "", sql, true, totalRows, (long)multi.ExecutionTimeMs);
                }
                else
                {
                    HasError = true;
                    ResultMessage = $"❌ {multi.ErrorMessage}";
                    _historyService.Add(new QueryHistoryEntry
                    {
                        Sql = sql.Trim(), Database = SelectedDatabase, Success = false
                    });
                }
            }
            else
            {
                // 單段 SQL — 也走 MultiResults 路徑以統一 Tab 顯示
                var trimmed  = sql.Trim().ToUpperInvariant();
                var sqlType  = trimmed.StartsWith("SELECT") || trimmed.StartsWith("SHOW") ||
                               trimmed.StartsWith("DESCRIBE") || trimmed.StartsWith("EXPLAIN") ? "SELECT"
                             : trimmed.StartsWith("INSERT") ? "INSERT"
                             : trimmed.StartsWith("UPDATE") ? "UPDATE"
                             : trimmed.StartsWith("DELETE") ? "DELETE"
                             : "DDL";
                bool isSelect = sqlType == "SELECT";

                QueryResult result;
                if (isSelect)
                {
                    result = await _connService.ExecuteQueryAsync(sql, SelectedDatabase);
                    if (result.Success)
                    {
                        var rs = new SingleResultSet
                        {
                            Index       = 0,
                            IsSelect    = true,
                            Data        = result.Data,
                            RowCount    = result.Data?.Rows.Count ?? 0,
                            Sql         = sql.Trim(),
                            SqlType     = sqlType,
                            ExecutionMs = result.ExecutionTimeMs
                        };
                        MultiResults.Add(rs);
                        HasMultiResults = true;
                        ActiveResult    = rs;
                        // ResultData set via ActiveResult → SetFullData
                        ResultMessage   = $"✅ {rs.RowCount:N0} 筆資料  |  耗時 {result.ExecutionTimeMs:F1} ms";
                        ParseEditableTable(sql);
                    }
                }
                else
                {
                    result = await _connService.ExecuteNonQueryAsync(sql, SelectedDatabase);
                    if (result.Success)
                    {
                        var rs = new SingleResultSet
                        {
                            Index        = 0,
                            IsSelect     = false,
                            RowsAffected = (int)result.RowsAffected,
                            Sql          = sql.Trim(),
                            SqlType      = sqlType,
                            ExecutionMs  = result.ExecutionTimeMs
                        };
                        MultiResults.Add(rs);
                        HasMultiResults = true;
                        ActiveResult    = rs;
                        ResultMessage   = $"✅ 影響 {result.RowsAffected:N0} 筆  |  耗時 {result.ExecutionTimeMs:F1} ms";
                    }
                }
                if (!result.Success) { HasError = true; ResultMessage = $"❌ {result.ErrorMessage}"; }
                _historyService.Add(new QueryHistoryEntry
                {
                    Sql = sql.Trim(), Database = SelectedDatabase,
                    Success = result.Success, ExecutionMs = result.ExecutionTimeMs,
                    RowsAffected = result.RowsAffected
                });
                App.AuditLogService.Log(_connService.CurrentProfile?.Name ?? "Unknown",
                    SelectedDatabase ?? "", sql, result.Success, (int)result.RowsAffected,
                    (long)result.ExecutionTimeMs, result.Success ? null : result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ResultMessage = $"❌ 例外：{ex.Message}";
        }
        finally { IsRunning = false; }
    }

    // ── 自動完成 ──────────────────────────────────────────────

    public async Task UpdateCompletionsAsync(string textBeforeCursor)
    {
        var word = GetCurrentWord(textBeforeCursor);
        if (word.Length < 1) { ShowCompletions = false; return; }

        var items = await _autoComplete.GetCompletionsAsync(textBeforeCursor, SelectedDatabase);
        _completions.Clear();
        foreach (var item in items) _completions.Add(item);
        ShowCompletions = _completions.Count > 0;
    }

    public void AcceptCompletion(CompletionItem item)
    {
        InsertTextRequested?.Invoke(item.Text);
        ShowCompletions = false;
    }

    private static string GetCurrentWord(string text)
    {
        var i = text.Length - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
            i--;
        return text[(i + 1)..];
    }

    // ── 匯出 ─────────────────────────────────────────────────

    private async Task ExportCsvAsync()
    {
        if (ResultData == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title  = "匯出 CSV",
            Filter = "CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
            FileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await App.ExportService.ExportCsvAsync(_fullData ?? ResultData, dlg.FileName);
            ResultMessage = $"✅ 已匯出 CSV：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            HasError = true;
            ResultMessage = $"❌ 匯出失敗：{ex.Message}";
        }
    }

    private async Task ExportSqlAsync()
    {
        if (_editDatabase == null || _editTable == null)
        {
            MessageBox.Show("請先執行 SELECT * FROM db.table 查詢，再進行 SQL 匯出。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title  = "匯出 SQL Dump",
            Filter = "SQL 檔案 (*.sql)|*.sql|所有檔案 (*.*)|*.*",
            FileName = $"{_editTable}_{DateTime.Now:yyyyMMdd}.sql"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            IsRunning = true;
            await App.ExportService.ExportTableSqlAsync(_editDatabase, _editTable, dlg.FileName);
            ResultMessage = $"✅ 已匯出 SQL：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            HasError = true;
            ResultMessage = $"❌ 匯出失敗：{ex.Message}";
        }
        finally { IsRunning = false; }
    }

    // ── 編輯模式（同上一版）────────────────────────────────────

    private void ToggleEdit(object? _ = null)
    {
        if (ResultData == null) return;
        if (IsEditMode && HasPendingChanges)
        {
            var r = MessageBox.Show("有未儲存的變更，確定要離開？", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            DiscardChanges();
        }
        IsEditMode = !IsEditMode;
        ResultMessage = IsEditMode
            ? "✏️ 編輯模式：直接修改儲存格，完成後按 💾 儲存"
            : "👁 唯讀模式";
    }

    private void ParseEditableTable(string sql)
    {
        _editTable = null; _editDatabase = null; _primaryKeys.Clear();
        try
        {
            var up = sql.ToUpperInvariant();
            var fromIdx = up.IndexOf(" FROM ");
            if (fromIdx < 0) return;
            var afterFrom = sql[(fromIdx + 6)..].TrimStart().Replace("`", "");
            var token = new string(afterFrom.TakeWhile(c => c != ' ' && c != '\n' && c != '\r').ToArray());
            if (token.Contains('.'))
            {
                var p = token.Split('.');
                _editDatabase = p[0]; _editTable = p[1];
            }
            else { _editDatabase = SelectedDatabase; _editTable = token; }
        }
        catch { }
    }

    public void OnCellBeginEdit(DataRowView rowView)
    {
        if (!IsEditMode) return;
        var row = rowView.Row;
        if (_newRows.Contains(row)) return;
        if (!_originalValues.ContainsKey(row))
        {
            var snap = new Dictionary<string, object?>();
            foreach (DataColumn col in row.Table.Columns)
                snap[col.ColumnName] = row[col, DataRowVersion.Current];
            _originalValues[row] = snap;
        }
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private void AddRow(object? _ = null)
    {
        if (ResultData == null) return;
        var row = ResultData.NewRow();
        ResultData.Rows.Add(row);
        _newRows.Add(row);
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private async Task DeleteRowAsync()
    {
        if (SelectedRow == null || _editDatabase == null || _editTable == null) return;
        if (MessageBox.Show("確定刪除？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;
        var row = SelectedRow.Row;
        if (_newRows.Contains(row))
        {
            _newRows.Remove(row); ResultData!.Rows.Remove(row);
            OnPropertyChanged(nameof(HasPendingChanges));
            ResultMessage = "✅ 已移除新增列"; return;
        }
        var pkVals = GetPkValues(row);
        var result = await _editService.DeleteRowAsync(_editDatabase, _editTable, pkVals);
        if (result.Success) { _originalValues.Remove(row); ResultData!.Rows.Remove(row); ResultMessage = "✅ 已刪除 1 筆"; }
        else { HasError = true; ResultMessage = $"❌ {result.ErrorMessage}"; }
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private async Task SaveChangesAsync()
    {
        if (_editDatabase == null || _editTable == null)
        { ResultMessage = "❌ 無法識別資料表"; HasError = true; return; }
        if (_primaryKeys.Count == 0)
            _primaryKeys = await _editService.GetPrimaryKeysAsync(_editDatabase, _editTable);
        if (_primaryKeys.Count == 0)
        { ResultMessage = "❌ 此資料表沒有主鍵"; HasError = true; return; }

        IsRunning = true; HasError = false;
        int saved = 0, failed = 0;
        try
        {
            foreach (var row in _newRows.ToList())
            {
                var vals = row.Table.Columns.Cast<DataColumn>()
                    .ToDictionary(c => c.ColumnName, c => row[c] == DBNull.Value ? null : row[c]);
                var r = await _editService.InsertRowAsync(_editDatabase, _editTable, vals);
                if (r.Success) { _newRows.Remove(row); saved++; }
                else { failed++; ResultMessage = $"❌ INSERT 失敗：{r.ErrorMessage}"; HasError = true; }
            }
            foreach (var (row, original) in _originalValues.ToList())
            {
                var newVals = row.Table.Columns.Cast<DataColumn>()
                    .ToDictionary(c => c.ColumnName, c => row[c] == DBNull.Value ? null : row[c]);
                var pkVals = _primaryKeys.ToDictionary(pk => pk,
                    pk => original.TryGetValue(pk, out var v) ? v : null);
                var r = await _editService.UpdateRowAsync(_editDatabase, _editTable, newVals, pkVals);
                if (r.Success) { _originalValues.Remove(row); saved++; }
                else { failed++; ResultMessage = $"❌ UPDATE 失敗：{r.ErrorMessage}"; HasError = true; }
            }
            if (failed == 0) ResultMessage = $"✅ 儲存成功 {saved} 筆 | {DateTime.Now:HH:mm:ss}";
        }
        finally { IsRunning = false; OnPropertyChanged(nameof(HasPendingChanges)); }
    }

    private void DiscardChanges(object? _ = null)
    {
        foreach (var row in _newRows.ToList()) ResultData?.Rows.Remove(row);
        _newRows.Clear();
        foreach (var (row, original) in _originalValues)
            foreach (var kv in original) row[kv.Key] = kv.Value ?? DBNull.Value;
        _originalValues.Clear();
        ResultMessage = "↩️ 已捨棄所有變更";
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private Dictionary<string, object?> GetPkValues(DataRow row) =>
        _primaryKeys.Where(pk => row.Table.Columns.Contains(pk))
                    .ToDictionary(pk => pk, pk => row[pk] == DBNull.Value ? null : row[pk]);

    private void ClearResult(object? _ = null)
    {
        SetFullData(null); ResultMessage = string.Empty;
        HasError = false; IsEditMode = false;
        _newRows.Clear(); _originalValues.Clear();
    }

    public new void OnPropertyChanged(string propertyName) => base.OnPropertyChanged(propertyName);

    /// <summary>簡易 SQL 分段（以分號分隔，忽略字串內的分號）</summary>
    private static List<string> SplitStatements(string sql)
    {
        var stmts = new List<string>();
        var sb    = new System.Text.StringBuilder();
        bool inStr = false; char strChar = ' ';
        foreach (char c in sql)
        {
            if (!inStr && (c == '\'' || c == '\"' || c == '`')) { inStr = true; strChar = c; sb.Append(c); }
            else if (inStr && c == strChar) { inStr = false; sb.Append(c); }
            else if (!inStr && c == ';')
            {
                var s = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(s)) stmts.Add(s);
                sb.Clear();
            }
            else sb.Append(c);
        }
        var last = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(last)) stmts.Add(last);
        return stmts.Count == 0 ? new List<string> { sql } : stmts;
    }

    // ── SQL 格式化 ────────────────────────────────────────────

    public event Action<string>? SqlFormatRequested;

    private void FormatSql(object? _ = null)
    {
        var formatted = Services.SqlFormatterService.Format(SqlText);
        if (formatted != SqlText)
        {
            SqlText = formatted;
            SqlFormatRequested?.Invoke(formatted);
        }
    }

    // ── AI 輔助 SQL ───────────────────────────────────────────

    private async Task AiGenerateSqlAsync()
    {
        if (string.IsNullOrWhiteSpace(AiPrompt)) return;
        AiIsLoading = true;
        AiResult = "AI 生成中...";
        try
        {
            var schemaHint = await BuildSchemaHintAsync();
            var result = await App.AiSqlService.GenerateSqlAsync(AiPrompt, schemaHint, SelectedDatabase);
            if (result.Success)
            {
                AiResult = result.Sql!;
                SqlText  = result.Sql!;
                SqlFormatRequested?.Invoke(result.Sql!);
            }
            else
            {
                AiResult = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            AiResult = $"❌ {ex.Message}";
        }
        finally { AiIsLoading = false; }
    }

    private async Task AiExplainSqlAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlText)) return;
        AiIsLoading = true;
        AiResult = "分析中...";
        try
        {
            var result = await App.AiSqlService.ExplainSqlAsync(SqlText, SelectedDatabase);
            AiResult   = result.Success ? $"💡 {result.Sql}" : $"❌ {result.Error}";
        }
        finally { AiIsLoading = false; }
    }

    private async Task ExportXlsxAsync()
    {
        if (ResultData == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"query_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            Filter     = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Services.ExportService.ExportXlsxAsync(_fullData ?? ResultData, dlg.FileName);
            ResultMessage = $"✅ 已匯出 Excel：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            ResultMessage = $"❌ Excel 匯出失敗：{ex.Message}";
            HasError      = true;
        }
    }

    private async Task<string> BuildSchemaHintAsync()
    {
        if (SelectedDatabase == null) return string.Empty;
        try
        {
            var tables = await _connService.GetTablesAsync(SelectedDatabase);
            var sb     = new System.Text.StringBuilder();
            foreach (var table in tables.Take(20))
            {
                var cols     = await _connService.GetColumnsAsync(SelectedDatabase, table);
                var colNames = string.Join(", ", cols.Select(c => c.Field).Take(15));
                sb.AppendLine($"{table}({colNames})");
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }
}