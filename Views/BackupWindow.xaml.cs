using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

// ── ViewModel ─────────────────────────────────────────────────

public class BackupViewModel : BaseViewModel
{
    private readonly ConnectionService _conn;

    public ObservableCollection<string> Databases { get; } = new();

    // ─ 備份 ───────────────────────────────────────────────────
    private string? _backupDatabase;
    public string? BackupDatabase { get => _backupDatabase; set { SetProperty(ref _backupDatabase, value); OnPropertyChanged(nameof(CanBackup)); } }

    private bool _includeDdl  = true;
    private bool _includeData = true;
    public bool IncludeDdl  { get => _includeDdl;  set => SetProperty(ref _includeDdl,  value); }
    public bool IncludeData { get => _includeData; set => SetProperty(ref _includeData, value); }

    private string _backupPath = string.Empty;
    public string BackupPath { get => _backupPath; set { SetProperty(ref _backupPath, value); OnPropertyChanged(nameof(CanBackup)); } }

    public bool CanBackup => !IsBusy && BackupDatabase != null && !string.IsNullOrEmpty(BackupPath);

    // ─ 還原 ───────────────────────────────────────────────────
    private string? _restoreDatabase;
    public string? RestoreDatabase { get => _restoreDatabase; set => SetProperty(ref _restoreDatabase, value); }

    private string _restorePath = string.Empty;
    public string RestorePath { get => _restorePath; set { SetProperty(ref _restorePath, value); OnPropertyChanged(nameof(CanRestore)); } }

    public bool CanRestore => !IsBusy && !string.IsNullOrEmpty(RestorePath) && File.Exists(RestorePath);

    // ─ 進度 ───────────────────────────────────────────────────
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(CanBackup)); OnPropertyChanged(nameof(CanRestore)); }
    }

    private bool _isIndeterminate;
    public bool IsIndeterminate { get => _isIndeterminate; set => SetProperty(ref _isIndeterminate, value); }

    private double _progressValue;
    public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

    private double _progressMax = 100;
    public double ProgressMax { get => _progressMax; set => SetProperty(ref _progressMax, value); }

    private string _statusText = "選擇資料庫與路徑後開始";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private bool _showOpenFolder;
    public bool ShowOpenFolder { get => _showOpenFolder; set => SetProperty(ref _showOpenFolder, value); }

    private string _lastOutputPath = string.Empty;
    public string LastOutputPath => _lastOutputPath;

    public BackupViewModel(ConnectionService conn)
    {
        _conn = conn;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var dbs = await _conn.GetDatabasesAsync();
        foreach (var db in dbs) Databases.Add(db);
        BackupDatabase = Databases.FirstOrDefault();

        // 預設備份路徑：桌面
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        BackupPath = Path.Combine(desktop, $"backup_{ts}.sql");
    }

    public async Task BackupAsync()
    {
        if (BackupDatabase == null || string.IsNullOrEmpty(BackupPath)) return;
        IsBusy = true;
        IsIndeterminate = false;
        ShowOpenFolder  = false;

        var tables = await _conn.GetTablesAsync(BackupDatabase);
        ProgressMax   = tables.Count * (IncludeData ? 2 : 1);
        ProgressValue = 0;

        var progress = new Progress<string>(msg =>
        {
            ProgressValue++;
            StatusText = msg;
        });

        try
        {
            await _conn.BackupDatabaseAsync(
                BackupDatabase, BackupPath, IncludeDdl, IncludeData, progress);

            var size = new FileInfo(BackupPath).Length;
            StatusText = $"✅ 備份完成！檔案大小：{FormatBytes(size)}  →  {Path.GetFileName(BackupPath)}";
            _lastOutputPath = Path.GetDirectoryName(BackupPath) ?? "";
            ShowOpenFolder  = true;
        }
        catch (Exception ex)
        {
            StatusText = $"❌ 備份失敗：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreAsync()
    {
        if (string.IsNullOrEmpty(RestorePath)) return;
        IsBusy = true;
        IsIndeterminate = true;
        StatusText = "還原中，請稍候…";

        try
        {
            var (ok, fail, errors) = await _conn.RestoreAsync(
                RestorePath, RestoreDatabase, null);

            IsIndeterminate = false;
            StatusText = fail == 0
                ? $"✅ 還原完成！成功執行 {ok:N0} 個語句"
                : $"⚠ 還原完成，成功 {ok:N0} / 失敗 {fail}（{errors.Count} 個錯誤）";

            if (errors.Count > 0 && errors.Count <= 5)
                MessageBox.Show(string.Join("\n", errors), "部分語句執行失敗",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            IsIndeterminate = false;
            StatusText = $"❌ 還原失敗：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatBytes(long b) => b switch
    {
        >= 1_048_576 => $"{b / 1_048_576.0:F1} MB",
        >= 1_024     => $"{b / 1_024.0:F1} KB",
        _            => $"{b} B"
    };
}

// ── Code-behind ───────────────────────────────────────────────

public partial class BackupWindow : Window
{
    private BackupViewModel Vm => (BackupViewModel)DataContext;

    public BackupWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        DataContext = new BackupViewModel(GetActiveConn());
    }

    private void BrowseBackup_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "選擇備份儲存位置",
            Filter     = "SQL 檔案 (*.sql)|*.sql|所有檔案|*.*",
            FileName   = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            DefaultExt = ".sql"
        };
        if (dlg.ShowDialog() == true) Vm.BackupPath = dlg.FileName;
    }

    private void BrowseRestore_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇 SQL 備份檔",
            Filter = "SQL 檔案 (*.sql)|*.sql|所有檔案|*.*"
        };
        if (dlg.ShowDialog() == true) Vm.RestorePath = dlg.FileName;
    }

    private async void Backup_Click(object s, RoutedEventArgs e)  => await Vm.BackupAsync();
    private async void Restore_Click(object s, RoutedEventArgs e) => await Vm.RestoreAsync();

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Vm.LastOutputPath) &&
            System.IO.Directory.Exists(Vm.LastOutputPath))
            System.Diagnostics.Process.Start("explorer.exe", Vm.LastOutputPath);
    }
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

}
