using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MySQLManager.Helpers;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

// ── ViewModel ─────────────────────────────────────────────────

public class ExplainViewModel : BaseViewModel
{
    private readonly ConnectionService _conn;

    private string _sql = string.Empty;
    public string Sql { get => _sql; set => SetProperty(ref _sql, value); }

    private string? _database;
    public string? Database { get => _database; set => SetProperty(ref _database, value); }

    public ObservableCollection<ExplainRowVm> ExplainRows { get; } = new();

    private bool _hasResult;
    public bool HasResult { get => _hasResult; set => SetProperty(ref _hasResult, value); }

    private string _overallGrade = "良";
    public string OverallGrade { get => _overallGrade; set => SetProperty(ref _overallGrade, value); }

    private string _overallColor = "#FFA726";
    public string OverallColor { get => _overallColor; set => SetProperty(ref _overallColor, value); }

    private string _suggestion = string.Empty;
    public string Suggestion { get => _suggestion; set => SetProperty(ref _suggestion, value); }

    public AsyncRelayCommand RunCommand { get; }

    public ExplainViewModel(ConnectionService conn, string initialSql, string? db)
    {
        _conn    = conn;
        Sql      = initialSql;
        Database = db;
        RunCommand = new AsyncRelayCommand(RunAsync);
        if (!string.IsNullOrWhiteSpace(Sql))
            _ = RunAsync();
    }

    public async Task RunAsync()
    {
        ExplainRows.Clear();
        HasResult = false;

        var rows = await _conn.GetExplainAsync(Sql, Database);
        if (rows.Count == 0) return;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ExplainRows.Add(new ExplainRowVm(r) { IsLast = i == rows.Count - 1 });
        }

        // 整體評分
        var minScore = ExplainRows.Min(r => r.PerformanceScore);
        OverallGrade = minScore switch { >= 90 => "優", >= 60 => "良", >= 30 => "差", _ => "慢" };
        OverallColor = ExplainRows.Min(r => r.PerformanceScore) switch
        {
            >= 90 => "#66BB6A", >= 60 => "#FFA726", >= 30 => "#FF7043", _ => "#EF5350"
        };

        // 建議
        var fullScans = ExplainRows.Where(r => r.Type == "ALL").ToList();
        var noIndex   = ExplainRows.Where(r => string.IsNullOrEmpty(r.Key) || r.Key == "—").ToList();
        Suggestion = (fullScans.Count, noIndex.Count) switch
        {
            (> 0, _) => $"⚠️ 偵測到全表掃描 ({string.Join(", ", fullScans.Select(r => r.Table))})，建議在 WHERE 欄位加索引",
            (_, > 0) => $"ℹ️ {noIndex.Count} 個步驟未使用索引，可考慮加入 INDEX",
            _        => "✅ 查詢計畫良好，所有步驟均有使用索引"
        };

        HasResult = true;
    }
}

// ExplainRow 包裝（加 IsLast 給 XAML 箭頭用）
public class ExplainRowVm : ExplainRow
{
    public bool IsLast { get; set; }
    public ExplainRowVm(ExplainRow src)
    {
        Id = src.Id; SelectType = src.SelectType; Table = src.Table;
        Partitions = src.Partitions; Type = src.Type;
        PossibleKeys = src.PossibleKeys; Key = src.Key; KeyLen = src.KeyLen;
        Ref = src.Ref; Rows = src.Rows; Filtered = src.Filtered; Extra = src.Extra;
    }
}

// ── Code-behind ───────────────────────────────────────────────

public partial class ExplainWindow : Window
{
    private ExplainViewModel Vm => (ExplainViewModel)DataContext;

    public ExplainWindow(string sql = "", string? database = null)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new ExplainViewModel(GetActiveConn(), sql, database);
        SqlInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.F5)
                await Vm.RunAsync();
        };
    }
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
