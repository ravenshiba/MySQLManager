using System;
using System.Collections.Generic;
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

public class RoutineEditorViewModel : BaseViewModel
{
    private readonly ConnectionService _conn;

    public ObservableCollection<string>      Databases        { get; } = new();
    public ObservableCollection<RoutineInfo> FilteredRoutines { get; } = new();
    private List<RoutineInfo> _allRoutines = new();

    private string? _selectedDatabase;
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set { SetProperty(ref _selectedDatabase, value); _ = LoadRoutinesAsync(); }
    }

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set { SetProperty(ref _filterText, value); ApplyFilter(); }
    }

    private RoutineInfo? _selectedRoutine;
    public RoutineInfo? SelectedRoutine
    {
        get => _selectedRoutine;
        set { SetProperty(ref _selectedRoutine, value); _ = LoadBodyAsync(); }
    }

    private string _currentTitle = "新建 Routine";
    public string CurrentTitle { get => _currentTitle; set => SetProperty(ref _currentTitle, value); }

    private string _statusText = "選擇左側 Routine 或新建";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }

    // 目前編輯的完整 SQL（由 View 透過 event 同步）
    public string CurrentCode { get; set; } = string.Empty;

    public event Action<string>? CodeChanged;   // ViewModel → View (更新編輯器內容)

    public RoutineEditorViewModel(ConnectionService conn)
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

    public async Task LoadRoutinesAsync()
    {
        if (SelectedDatabase == null) return;
        _allRoutines = await _conn.GetRoutinesAsync(SelectedDatabase);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredRoutines.Clear();
        var kw = FilterText.Trim();
        foreach (var r in _allRoutines.Where(r =>
            string.IsNullOrEmpty(kw) ||
            r.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            FilteredRoutines.Add(r);
    }

    private async Task LoadBodyAsync()
    {
        if (_selectedRoutine == null || SelectedDatabase == null) return;
        if (IsDirty)
        {
            var res = MessageBox.Show("目前有未儲存的修改，確定要切換嗎？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
        }

        var body = await _conn.GetRoutineBodyAsync(
            SelectedDatabase, _selectedRoutine.Name, _selectedRoutine.Type);

        CurrentTitle = $"{_selectedRoutine.Icon} {_selectedRoutine.Name}";
        CodeChanged?.Invoke(body);
        IsDirty = false;
        StatusText = $"已載入 {_selectedRoutine.Type} `{_selectedRoutine.Name}`";
        HasError = false;
    }

    public void SetTemplate(string type)
    {
        var db = SelectedDatabase ?? "mydb";
        CurrentTitle = $"新建 {type}";
        IsDirty = false;

        string template = type == "FUNCTION"
            ? $@"DELIMITER $$

CREATE FUNCTION `{db}`.`new_function` (param1 INT)
RETURNS INT
DETERMINISTIC
BEGIN
    DECLARE result INT;
    SET result = param1 * 2;
    RETURN result;
END$$

DELIMITER ;"
            : $@"DELIMITER $$

CREATE PROCEDURE `{db}`.`new_procedure` (IN param1 INT, OUT result VARCHAR(255))
BEGIN
    -- Write your procedure logic here
    SET result = CONCAT('Input was: ', param1);
END$$

DELIMITER ;";

        CodeChanged?.Invoke(template);
        StatusText = $"已建立 {type} 範本";
        HasError   = false;
    }

    public async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentCode)) return;
        HasError = false;
        StatusText = "執行中…";

        // 移除 DELIMITER 指令後直接執行 body
        var sql = StripDelimiter(CurrentCode);
        var result = await _conn.ExecuteNonQueryAsync(sql, SelectedDatabase);
        HasError   = !result.Success;
        StatusText = result.Success
            ? $"✅ 執行成功 | 耗時 {result.ExecutionTimeMs:F0} ms"
            : $"❌ {result.ErrorMessage}";

        if (result.Success)
        {
            await LoadRoutinesAsync();
            IsDirty = false;
        }
    }

    public async Task SaveAsync()
    {
        // DROP + CREATE
        if (_selectedRoutine != null && SelectedDatabase != null)
        {
            await _conn.DropRoutineAsync(SelectedDatabase,
                _selectedRoutine.Name, _selectedRoutine.Type);
        }
        await ExecuteAsync();
    }

    public async Task DeleteAsync()
    {
        if (_selectedRoutine == null || SelectedDatabase == null) return;
        var res = MessageBox.Show(
            $"確定刪除 {_selectedRoutine.Type} `{_selectedRoutine.Name}`？",
            "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        var result = await _conn.DropRoutineAsync(
            SelectedDatabase, _selectedRoutine.Name, _selectedRoutine.Type);
        HasError = !result.Success;
        StatusText = result.Success ? "✅ 已刪除" : $"❌ {result.ErrorMessage}";
        if (result.Success)
        {
            await LoadRoutinesAsync();
            SelectedRoutine = null;
            CodeChanged?.Invoke(string.Empty);
            CurrentTitle = "新建 Routine";
        }
    }

    private static string StripDelimiter(string sql)
    {
        // 移除 DELIMITER $$ ... DELIMITER ; 包裝，取出主體
        var lines = sql.Split('\n');
        var result = new System.Text.StringBuilder();
        bool inBody = false;
        string delim = "$$";

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var upper = line.TrimStart().ToUpperInvariant();

            if (upper.StartsWith("DELIMITER"))
            {
                var parts = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var newDelim = parts[1];
                    if (newDelim == ";") { inBody = false; }
                    else { delim = newDelim; inBody = true; }
                }
                continue;
            }

            if (inBody)
            {
                var stripped = line.TrimEnd().TrimEnd(delim.ToCharArray()).TrimEnd();
                result.AppendLine(stripped);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                result.AppendLine(line);
            }
        }
        return result.ToString().Trim();
    }
}

// ── Code-behind ───────────────────────────────────────────────

public partial class RoutineEditorWindow : Window
{
    private RoutineEditorViewModel Vm => (RoutineEditorViewModel)DataContext;
    private bool _suppressChange;

    public RoutineEditorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new RoutineEditorViewModel(GetActiveConn());

        Vm.CodeChanged += code =>
        {
            _suppressChange = true;
            CodeEditor.Text = code;
            _suppressChange = false;
            Vm.CurrentCode = code;
        };

        CodeEditor.TextChanged += (_, _) =>
        {
            if (_suppressChange) return;
            Vm.CurrentCode = CodeEditor.Text;
            Vm.IsDirty = true;
        };

        TrySetSyntax();
    }

    private void TrySetSyntax()
    {
        try
        {
            CodeEditor.SyntaxHighlighting =
                ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
                    .GetDefinition("SQL");
        }
        catch { }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5)
        { _ = Vm.ExecuteAsync(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        { _ = Vm.SaveAsync(); e.Handled = true; }
    }

    private async void Execute_Click(object s, RoutedEventArgs e) => await Vm.ExecuteAsync();
    private async void Save_Click(object s, RoutedEventArgs e)    => await Vm.SaveAsync();
    private async void Refresh_Click(object s, RoutedEventArgs e) => await Vm.LoadRoutinesAsync();
    private void NewProcedure_Click(object s, RoutedEventArgs e)  => Vm.SetTemplate("PROCEDURE");
    private void NewFunction_Click(object s, RoutedEventArgs e)   => Vm.SetTemplate("FUNCTION");
    private async void DeleteRoutine_Click(object s, RoutedEventArgs e) => await Vm.DeleteAsync();
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
