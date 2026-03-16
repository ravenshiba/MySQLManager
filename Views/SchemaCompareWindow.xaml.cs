using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

// ── ViewModel ─────────────────────────────────────────────────

public class SchemaCompareViewModel : BaseViewModel
{
    private readonly SchemaCompareService _svc;
    private readonly ConnectionService    _conn;

    public ObservableCollection<string>           Databases { get; } = new();
    public ObservableCollection<TableComparePair> Pairs     { get; } = new();

    private string? _leftDatabase;
    public string? LeftDatabase  { get => _leftDatabase;  set => SetProperty(ref _leftDatabase,  value); }
    private string? _rightDatabase;
    public string? RightDatabase { get => _rightDatabase; set => SetProperty(ref _rightDatabase, value); }

    private TableComparePair? _selectedPair;
    public TableComparePair? SelectedPair
    {
        get => _selectedPair;
        set { SetProperty(ref _selectedPair, value); OnPropertyChanged(nameof(SelectedPairAllDiffs)); }
    }

    public IEnumerable<SchemaDiffItem> SelectedPairAllDiffs =>
        _selectedPair == null ? Enumerable.Empty<SchemaDiffItem>() :
        _selectedPair.ColumnDiffs.Concat(_selectedPair.IndexDiffs)
                     .OrderBy(d => d.Kind == DiffType.Same ? 1 : 0);

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; set => SetProperty(ref _hasResult, value); }

    private string _progressText = string.Empty;
    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    private string _statusText = "選擇兩個資料庫後按「開始比較」";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // 統計
    public int AddedCount   => Pairs.Count(p => p.Kind == DiffType.Added);
    public int RemovedCount => Pairs.Count(p => p.Kind == DiffType.Removed);
    public int ModifiedCount => Pairs.Count(p => p.Kind == DiffType.Same && p.DiffCount > 0);
    public int SameCount    => Pairs.Count(p => p.Kind == DiffType.Same && p.DiffCount == 0);

    public AsyncRelayCommand CompareCommand      { get; }
    public RelayCommand      CopyAlterSqlCommand { get; }

    public SchemaCompareViewModel(ConnectionService conn)
    {
        _conn = conn;
        _svc  = new SchemaCompareService(conn);
        CompareCommand      = new AsyncRelayCommand(DoCompareAsync, () => !IsLoading && LeftDatabase != null && RightDatabase != null);
        CopyAlterSqlCommand = new RelayCommand(_ => CopyAlterSql());
        LoadDatabases();
    }

    private async void LoadDatabases()
    {
        var dbs = await _conn.GetDatabasesAsync();
        foreach (var db in dbs) Databases.Add(db);
        if (Databases.Count >= 1) LeftDatabase  = Databases[0];
        if (Databases.Count >= 2) RightDatabase = Databases[1];
    }

    private async Task DoCompareAsync()
    {
        if (LeftDatabase == null || RightDatabase == null) return;

        IsLoading = true;
        HasResult = false;
        Pairs.Clear();
        SelectedPair = null;

        var progress = new Progress<string>(msg => ProgressText = msg);

        var pairs = await _svc.CompareAsync(LeftDatabase, RightDatabase, progress);
        foreach (var p in pairs) Pairs.Add(p);

        if (Pairs.Count > 0) SelectedPair = Pairs[0];

        HasResult = true;
        IsLoading = false;

        RefreshStats();
        StatusText = $"比較完成：{AddedCount} 新增、{RemovedCount} 移除、{ModifiedCount} 有差異、{SameCount} 相同";
    }

    private void RefreshStats()
    {
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(SameCount));
    }

    private void CopyAlterSql()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- Schema diff: {LeftDatabase} → {RightDatabase}");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var pair in Pairs.Where(p => p.Kind != DiffType.Same || p.DiffCount > 0))
        {
            if (pair.Kind == DiffType.Added)
            {
                sb.AppendLine($"-- Table `{pair.TableName}` only in {RightDatabase} (needs CREATE in {LeftDatabase})");
                continue;
            }
            if (pair.Kind == DiffType.Removed)
            {
                sb.AppendLine($"-- Table `{pair.TableName}` only in {LeftDatabase}");
                continue;
            }

            sb.AppendLine($"-- ALTER TABLE `{pair.TableName}`");
            foreach (var diff in pair.ColumnDiffs.Where(d => d.Kind != DiffType.Same))
            {
                sb.AppendLine(diff.Kind switch
                {
                    DiffType.Added   => $"ALTER TABLE `{pair.TableName}` ADD COLUMN `{diff.Name}` {diff.RightValue?.Split('|')[0].Trim()};",
                    DiffType.Removed => $"ALTER TABLE `{pair.TableName}` DROP COLUMN `{diff.Name}`;",
                    DiffType.Modified => $"ALTER TABLE `{pair.TableName}` MODIFY COLUMN `{diff.Name}` -- LEFT: {diff.LeftValue} | RIGHT: {diff.RightValue}",
                    _ => ""
                });
            }
            sb.AppendLine();
        }

        Clipboard.SetText(sb.ToString());
        StatusText = "✅ ALTER SQL 已複製到剪貼簿";
    }
}

// ── Code-behind ───────────────────────────────────────────────

public partial class SchemaCompareWindow : Window
{
    public SchemaCompareWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new SchemaCompareViewModel(GetActiveConn());
    }
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
