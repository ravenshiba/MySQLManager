using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

// ── ViewModel ─────────────────────────────────────────────────

public class CsvImportViewModel : BaseViewModel
{
    private readonly ConnectionService _conn;
    private readonly CsvImportService  _svc;

    // ─ 檔案選擇 ──────────────────────────────────────────────

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set { SetProperty(ref _filePath, value); HasPreview = false; HasMapping = false; }
    }

    public List<string> Delimiters { get; } = new() { ",", ";", "\\t", "|" };

    private string _selectedDelimiter = ",";
    public string SelectedDelimiter { get => _selectedDelimiter; set => SetProperty(ref _selectedDelimiter, value); }

    private bool _hasHeader = true;
    public bool HasHeader { get => _hasHeader; set => SetProperty(ref _hasHeader, value); }

    // ─ 目標 ──────────────────────────────────────────────────

    public ObservableCollection<string> Databases { get; } = new();
    public ObservableCollection<string> Tables    { get; } = new();
    public ObservableCollection<string> TableColumns { get; } = new();

    private string? _targetDatabase;
    public string? TargetDatabase
    {
        get => _targetDatabase;
        set { SetProperty(ref _targetDatabase, value); _ = LoadTablesAsync(); }
    }

    private string? _targetTable;
    public string? TargetTable
    {
        get => _targetTable;
        set { SetProperty(ref _targetTable, value); _ = LoadColumnsAndAutoMapAsync(); }
    }

    private bool _skipErrors = true;
    public bool SkipErrors { get => _skipErrors; set => SetProperty(ref _skipErrors, value); }

    // ─ 對映 ──────────────────────────────────────────────────

    public ObservableCollection<CsvColumn> Mapping { get; } = new();

    private bool _hasPreview;
    public bool HasPreview { get => _hasPreview; set => SetProperty(ref _hasPreview, value); }

    private bool _hasMapping;
    public bool HasMapping { get => _hasMapping; set => SetProperty(ref _hasMapping, value); }

    private string _previewInfo = string.Empty;
    public string PreviewInfo { get => _previewInfo; set => SetProperty(ref _previewInfo, value); }

    // ─ 進度 ──────────────────────────────────────────────────

    private bool _isImporting;
    public bool IsImporting { get => _isImporting; set => SetProperty(ref _isImporting, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

    private double _progressMax = 100;
    public double ProgressMax { get => _progressMax; set => SetProperty(ref _progressMax, value); }

    private string _statusText = "請選擇 CSV 檔案";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public bool CanImport => HasMapping && TargetTable != null && !IsImporting;

    // ─────────────────────────────────────────────────────────

    public CsvImportViewModel(ConnectionService conn)
    {
        _conn = conn;
        _svc  = new CsvImportService(conn);
        _ = LoadDatabasesAsync();
    }

    private async Task LoadDatabasesAsync()
    {
        var dbs = await _conn.GetDatabasesAsync();
        foreach (var db in dbs) Databases.Add(db);
        TargetDatabase = Databases.FirstOrDefault();
    }

    private async Task LoadTablesAsync()
    {
        Tables.Clear();
        if (TargetDatabase == null) return;
        var tables = await _conn.GetTablesAsync(TargetDatabase);
        foreach (var t in tables) Tables.Add(t);
    }

    private async Task LoadColumnsAndAutoMapAsync()
    {
        TableColumns.Clear();
        if (TargetDatabase == null || TargetTable == null) return;

        var cols = await _svc.GetTableColumnsAsync(TargetDatabase, TargetTable);
        foreach (var c in cols) TableColumns.Add(c);

        // 自動對映：名稱相同（不分大小寫）
        foreach (var m in Mapping)
        {
            m.MappedColumn = TableColumns.FirstOrDefault(c =>
                c.Equals(m.CsvHeader, StringComparison.OrdinalIgnoreCase));
        }

        HasMapping = Mapping.Count > 0;
        OnPropertyChanged(nameof(CanImport));
    }

    public void AnalyzeFile()
    {
        if (string.IsNullOrWhiteSpace(FilePath)) return;
        try
        {
            var delim = SelectedDelimiter == "\\t" ? '\t' : SelectedDelimiter[0];
            var (headers, rows) = _svc.ReadPreview(FilePath, 3, delim, HasHeader);

            Mapping.Clear();
            for (int i = 0; i < headers.Count; i++)
            {
                var sample = rows.Select(r => i < r.Count ? r[i] : "").FirstOrDefault() ?? "";
                Mapping.Add(new CsvColumn { CsvHeader = headers[i], SampleValue = sample });
            }

            var totalLines = System.IO.File.ReadAllLines(FilePath).Length;
            var dataRows   = HasHeader ? totalLines - 1 : totalLines;
            PreviewInfo    = $"📄 {System.IO.Path.GetFileName(FilePath)}  |  {headers.Count} 欄  |  約 {dataRows:N0} 列資料";
            HasPreview = true;
            StatusText = "✅ 分析完成，請選擇目標資料表並確認欄位對映";
            _ = LoadColumnsAndAutoMapAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"❌ 分析失敗：{ex.Message}";
        }
    }

    public async Task ImportAsync()
    {
        if (TargetDatabase == null || TargetTable == null) return;
        IsImporting = true;
        ProgressValue = 0;
        OnPropertyChanged(nameof(CanImport));

        var totalLines = System.IO.File.ReadAllLines(FilePath).Length;
        ProgressMax = HasHeader ? totalLines - 1 : totalLines;

        var progress = new Progress<(int done, int total)>(p =>
        {
            ProgressValue = p.done;
            StatusText = $"匯入中… {p.done:N0} / {p.total:N0}";
        });

        var delim = SelectedDelimiter == "\\t" ? '\t' : SelectedDelimiter[0];
        var (imported, failed, error) = await _svc.ImportAsync(
            FilePath, TargetDatabase, TargetTable,
            Mapping.ToList(), delim, HasHeader, SkipErrors, progress);

        IsImporting = false;
        OnPropertyChanged(nameof(CanImport));

        if (error != null && !SkipErrors)
            StatusText = $"❌ 匯入中斷：{error}";
        else
            StatusText = $"✅ 匯入完成：{imported:N0} 筆成功，{failed:N0} 筆失敗";
    }
}

// ── Code-behind ───────────────────────────────────────────────

public partial class CsvImportWindow : Window
{
    private CsvImportViewModel Vm => (CsvImportViewModel)DataContext;

    public CsvImportWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new CsvImportViewModel(GetActiveConn());
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇 CSV 檔案",
            Filter = "CSV 檔案 (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|所有檔案|*.*"
        };
        if (dlg.ShowDialog() == true)
            Vm.FilePath = dlg.FileName;
    }

    private void Analyze_Click(object sender, RoutedEventArgs e) => Vm.AnalyzeFile();
    private async void Import_Click(object sender, RoutedEventArgs e) => await Vm.ImportAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
