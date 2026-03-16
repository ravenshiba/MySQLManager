using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MySQLManager.Helpers;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

public class DataDiffViewModel : ObservableObject
{
    private readonly ConnectionService _conn;
    private readonly DataDiffService   _svc;
    private DataDiffResult?            _lastResult;

    public DataDiffViewModel(ConnectionService conn)
    {
        _conn = conn;
        _svc  = new DataDiffService(conn);
        CompareCommand      = new AsyncRelayCommand(RunCompareAsync,  () => CanCompare);
        ExportSqlCommand    = new AsyncRelayCommand(ExportSqlAsync,   () => _lastResult != null);
        CopyPatchCommand    = new RelayCommand(_    => CopyPatch(),    _ => _lastResult != null);
        ShowOnlyDiffCommand = new RelayCommand(_    => ToggleOnlyDiff());
    }

    // ── 選項 ──────────────────────────────────────────────────

    public ObservableCollection<string> Databases  { get; } = new();
    public ObservableCollection<string> LeftTables { get; } = new();
    public ObservableCollection<string> RightTables{ get; } = new();

    private string? _leftDb;
    public string? LeftDatabase
    {
        get => _leftDb;
        set { SetProperty(ref _leftDb, value); _ = LoadLeftTablesAsync(); OnPropertyChanged(nameof(CanCompare)); }
    }

    private string? _leftTable;
    public string? LeftTable
    {
        get => _leftTable;
        set { SetProperty(ref _leftTable, value); OnPropertyChanged(nameof(CanCompare)); }
    }

    private string? _rightDb;
    public string? RightDatabase
    {
        get => _rightDb;
        set { SetProperty(ref _rightDb, value); _ = LoadRightTablesAsync(); OnPropertyChanged(nameof(CanCompare)); }
    }

    private string? _rightTable;
    public string? RightTable
    {
        get => _rightTable;
        set { SetProperty(ref _rightTable, value); OnPropertyChanged(nameof(CanCompare)); }
    }

    private string? _keyColumn;
    public string? KeyColumn
    {
        get => _keyColumn;
        set => SetProperty(ref _keyColumn, value);
    }

    private string? _whereClause;
    public string? WhereClause
    {
        get => _whereClause;
        set => SetProperty(ref _whereClause, value);
    }

    private bool _showUnchanged = false;
    public bool ShowUnchanged
    {
        get => _showUnchanged;
        set { SetProperty(ref _showUnchanged, value); ApplyFilter(); }
    }

    // ── 狀態 ──────────────────────────────────────────────────

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(CanCompare)); }
    }

    private string _statusText = "選擇兩個資料表後點擊「開始比對」";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private int _addedCount, _deletedCount, _modifiedCount, _unchangedCount;
    public int AddedCount    { get => _addedCount;    set => SetProperty(ref _addedCount, value); }
    public int DeletedCount  { get => _deletedCount;  set => SetProperty(ref _deletedCount, value); }
    public int ModifiedCount { get => _modifiedCount; set => SetProperty(ref _modifiedCount, value); }
    public int UnchangedCount{ get => _unchangedCount;set => SetProperty(ref _unchangedCount, value); }
    public bool HasResult    => _lastResult != null;
    public bool CanCompare   => !IsLoading && LeftDatabase != null && LeftTable != null
                                           && RightDatabase != null && RightTable != null;

    // ── 顯示資料 ──────────────────────────────────────────────

    // 所有欄位（動態產生列）
    private List<string> _columns = new();
    public List<string> Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    // 全部 diff 行（含 Unchanged）
    private List<DiffRow> _allRows = new();

    // 目前顯示行
    public ObservableCollection<DiffRow> DisplayRows { get; } = new();

    // ── 命令 ──────────────────────────────────────────────────

    public AsyncRelayCommand CompareCommand      { get; }
    public AsyncRelayCommand ExportSqlCommand    { get; }
    public RelayCommand      CopyPatchCommand    { get; }
    public RelayCommand      ShowOnlyDiffCommand { get; }

    // ── 初始化 ────────────────────────────────────────────────

    public async Task LoadDatabasesAsync()
    {
        try
        {
            var dbs = await _conn.GetDatabasesAsync();
            Databases.Clear();
            foreach (var db in dbs) Databases.Add(db);
        }
        catch (Exception ex)
        {
            StatusText = $"載入資料庫失敗：{ex.Message}";
        }
    }

    private async Task LoadLeftTablesAsync()
    {
        if (_leftDb == null) return;
        LeftTables.Clear();
        try
        {
            var tbls = await _conn.GetTablesAsync(_leftDb);
            foreach (var t in tbls) LeftTables.Add(t);
        }
        catch { }
    }

    private async Task LoadRightTablesAsync()
    {
        if (_rightDb == null) return;
        RightTables.Clear();
        try
        {
            var tbls = await _conn.GetTablesAsync(_rightDb);
            foreach (var t in tbls) RightTables.Add(t);
        }
        catch { }
    }

    // ── 比對 ──────────────────────────────────────────────────

    private async Task RunCompareAsync()
    {
        IsLoading = true;
        StatusText = "比對中...";
        _allRows.Clear();
        DisplayRows.Clear();
        Columns = new();
        _lastResult = null;

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            _lastResult = await _svc.CompareAsync(
                LeftDatabase!,  LeftTable!,
                RightDatabase!, RightTable!,
                string.IsNullOrWhiteSpace(KeyColumn) ? null : KeyColumn.Trim(),
                string.IsNullOrWhiteSpace(WhereClause) ? null : WhereClause.Trim(),
                progress: progress);

            AddedCount    = _lastResult.AddedCount;
            DeletedCount  = _lastResult.DeletedCount;
            ModifiedCount = _lastResult.ModifiedCount;
            UnchangedCount= _lastResult.UnchangedCount;
            Columns       = _lastResult.Columns;
            KeyColumn     = _lastResult.KeyColumn;

            _allRows = _lastResult.Rows;
            ApplyFilter();

            int total = _lastResult.Rows.Count;
            StatusText = _lastResult.TotalDiff == 0
                ? $"✅ 兩表資料完全相同（共 {total:N0} 筆）"
                : $"比對完成：{total:N0} 筆  ·  新增 {AddedCount}  刪除 {DeletedCount}  修改 {ModifiedCount}  相同 {UnchangedCount}";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasResult));
            ExportSqlCommand.RaiseCanExecuteChanged();
            CopyPatchCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyFilter()
    {
        DisplayRows.Clear();
        foreach (var row in _allRows)
            if (ShowUnchanged || row.Kind != DiffRowKind.Unchanged)
                DisplayRows.Add(row);
    }

    private void ToggleOnlyDiff()
    {
        ShowUnchanged = !ShowUnchanged;
    }

    // ── 匯出 ──────────────────────────────────────────────────

    private async Task ExportSqlAsync()
    {
        if (_lastResult == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter   = "SQL 檔案 (*.sql)|*.sql",
            FileName = $"diff_{LeftTable}_vs_{RightTable}_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        };
        if (dlg.ShowDialog() != true) return;
        var sql = DataDiffService.GeneratePatchSql(_lastResult, RightDatabase!, RightTable!);
        await System.IO.File.WriteAllTextAsync(dlg.FileName, sql, System.Text.Encoding.UTF8);
        StatusText = $"✅ 已匯出 SQL patch → {dlg.FileName}";
    }

    private void CopyPatch()
    {
        if (_lastResult == null) return;
        var sql = DataDiffService.GeneratePatchSql(_lastResult, RightDatabase!, RightTable!);
        Clipboard.SetText(sql);
        StatusText = "✅ SQL patch 已複製到剪貼簿";
    }
}
