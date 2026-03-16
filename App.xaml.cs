using System.Windows;
using MySQLManager.Services;

namespace MySQLManager;

public partial class App : Application
{
    public static ConnectionService      ConnectionService   { get; private set; } = null!;
    public static SettingsService        SettingsService     { get; private set; } = null!;
    public static QueryHistoryService    HistoryService      { get; private set; } = null!;
    public static SqlAutoCompleteService AutoCompleteService { get; private set; } = null!;
    public static ExportService          ExportService       { get; private set; } = null!;
    public static AiSqlService           AiSqlService        { get; private set; } = null!;
    public static AuditLogService        AuditLogService     { get; private set; } = null!;
    public static DataDiffService        DataDiffService     { get; private set; } = null!;
    public static SnippetService            SnippetService         { get; private set; } = null!;
    public static ScheduledBackupService   ScheduledBackupService { get; private set; } = null!;
    public static ThemeService            ThemeService           { get; private set; } = null!;
    public static LocalizationService     LocalizationService    { get; private set; } = null!;
    public static ColumnWidthService       ColumnWidthService      { get; private set; } = null!;
    public static UserManagementService   UserManagementService  { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsService     = new SettingsService();
        ConnectionService   = new ConnectionService();
        HistoryService      = new QueryHistoryService();
        AutoCompleteService = new SqlAutoCompleteService(ConnectionService);
        ExportService       = new ExportService(ConnectionService);
        AiSqlService        = new AiSqlService { ApiKey = SettingsService.LoadAiApiKey() };
        AuditLogService     = new AuditLogService();
        DataDiffService     = new DataDiffService(ConnectionService);
        SnippetService         = new SnippetService();
        ScheduledBackupService = new ScheduledBackupService(ConnectionService);
        ScheduledBackupService.StartScheduler();
        ThemeService = new ThemeService();
        LocalizationService = new LocalizationService(SettingsService);
        LocalizationService.Apply();
        ColumnWidthService = new ColumnWidthService();
        ThemeService.Apply();
        UserManagementService = new UserManagementService(ConnectionService);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ConnectionService?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 讓視窗在任何螢幕大小都能完整顯示（不超出工作區域）
    /// 在視窗 Loaded 事件中呼叫：  App.FitWindowToScreen(this);
    /// </summary>
    public static void FitWindowToScreen(System.Windows.Window win)
    {
        var area = System.Windows.SystemParameters.WorkArea;
        if (win.Width  > area.Width)  win.Width  = area.Width  * 0.92;
        if (win.Height > area.Height) win.Height = area.Height * 0.92;
        // 重新置中（CenterOwner/CenterScreen 在縮小後可能偏移）
        if (win.WindowStartupLocation == System.Windows.WindowStartupLocation.CenterOwner
            && win.Owner != null)
        {
            win.Left = win.Owner.Left + (win.Owner.Width  - win.Width)  / 2;
            win.Top  = win.Owner.Top  + (win.Owner.Height - win.Height) / 2;
        }
        else
        {
            win.Left = area.Left + (area.Width  - win.Width)  / 2;
            win.Top  = area.Top  + (area.Height - win.Height) / 2;
        }
        // 確保不超出螢幕邊界
        if (win.Left < area.Left) win.Left = area.Left + 8;
        if (win.Top  < area.Top)  win.Top  = area.Top  + 8;
    }
}
