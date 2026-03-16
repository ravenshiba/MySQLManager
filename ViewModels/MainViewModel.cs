using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Models;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;

    // ══════════════════════════════════════════════════════════
    // 多連線管理
    // ══════════════════════════════════════════════════════════

    public ObservableCollection<ConnectionSession> Sessions { get; } = new();

    private ConnectionSession? _activeSession;
    public ConnectionSession? ActiveSession
    {
        get => _activeSession;
        set
        {
            // 清除舊 session 的 active 狀態
            if (_activeSession != null) _activeSession.IsActive = false;
            if (SetProperty(ref _activeSession, value))
            {
                if (value != null) value.IsActive = true;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(ServerVersion));
                OnPropertyChanged(nameof(ConnectedHost));
                OnPropertyChanged(nameof(ConnectedUser));
                OnPropertyChanged(nameof(SelectedDatabase));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(TreeNodes));
                SyncQueryTabsFromSession();
            }
        }
    }

    // ── 代理屬性：從 ActiveSession 取值 ──────────────────────

    public bool IsConnected     => ActiveSession?.IsConnected ?? false;
    public bool IsBusy          => ActiveSession?.IsBusy ?? false;
    public string ServerVersion => ActiveSession?.ServerVersion ?? "";
    public string ConnectedHost => ActiveSession?.ConnectedHost ?? "";
    public string ConnectedUser => ActiveSession?.ConnectedUser ?? "";
    public string? SelectedDatabase
    {
        get => ActiveSession?.SelectedDatabase;
        set { if (ActiveSession != null) ActiveSession.SelectedDatabase = value; }
    }

    private string _statusText = "未連線";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ObservableCollection<DbTreeNode> TreeNodes => ActiveSession?.TreeNodes ?? _emptyNodes;
    private readonly ObservableCollection<DbTreeNode> _emptyNodes = new();

    // ── 目前 Session 的 ConnectionService 捷徑 ───────────────
    private ConnectionService ConnSvc => ActiveSession?.ConnectionService
        ?? throw new InvalidOperationException("No active session");

    // ══════════════════════════════════════════════════════════
    // 查詢分頁
    // ══════════════════════════════════════════════════════════

    public ObservableCollection<QueryTabViewModel> QueryTabs { get; } = new();

    private QueryTabViewModel? _activeTab;
    public QueryTabViewModel? ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }

    // ══════════════════════════════════════════════════════════
    // 命令
    // ══════════════════════════════════════════════════════════

    public AsyncRelayCommand ConnectCommand     { get; }
    public AsyncRelayCommand DisconnectCommand  { get; }
    public RelayCommand      NewQueryTabCommand { get; }
    public RelayCommand      CloseTabCommand    { get; }
    public AsyncRelayCommand RefreshTreeCommand { get; }
    public RelayCommand      CloseSessionCommand { get; }
    public RelayCommand      SwitchSessionCommand { get; }

    public MainViewModel()
    {
        _settingsService = App.SettingsService;

        ConnectCommand        = new AsyncRelayCommand(OpenConnectionDialogAsync);
        DisconnectCommand     = new AsyncRelayCommand(DisconnectCurrentAsync, () => IsConnected);
        NewQueryTabCommand    = new RelayCommand(AddNewQueryTab);
        CloseTabCommand       = new RelayCommand(CloseTab);
        RefreshTreeCommand    = new AsyncRelayCommand(RefreshTreeAsync, () => IsConnected);
        CloseSessionCommand   = new RelayCommand(CloseSession);
        SwitchSessionCommand  = new RelayCommand(SwitchSession);

        // 確保至少有一個空 Session 讓 UI 不報 null
        AddNewSession();
        // 初始 session 設為 active
        if (Sessions.Count > 0) Sessions[0].IsActive = true;
    }

    // ══════════════════════════════════════════════════════════
    // Session 管理
    // ══════════════════════════════════════════════════════════

    private void AddNewSession()
    {
        var session = new ConnectionSession { DisplayName = "未連線" };
        session.PropertyChanged += (_, e) =>
        {
            if (session == ActiveSession)
            {
                if (e.PropertyName is nameof(ConnectionSession.IsConnected)
                    or nameof(ConnectionSession.IsBusy)
                    or nameof(ConnectionSession.ServerVersion)
                    or nameof(ConnectionSession.ConnectedHost)
                    or nameof(ConnectionSession.ConnectedUser)
                    or nameof(ConnectionSession.SelectedDatabase))
                {
                    OnPropertyChanged(e.PropertyName);
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        };
        Sessions.Add(session);
        ActiveSession = session;
    }

    private void CloseSession(object? param)
    {
        var session = param as ConnectionSession ?? ActiveSession;
        if (session == null) return;

        // 至少保留一個 Session
        if (Sessions.Count == 1)
        {
            session.ConnectionService.DisconnectAsync().GetAwaiter().GetResult();
            session.IsConnected = false;
            session.DisplayName = "未連線";
            session.TreeNodes.Clear();
            // 清除此 session 對應的 QueryTabs
            QueryTabs.Clear();
            ActiveTab = null;
            StatusText = "未連線";
            return;
        }

        var idx = Sessions.IndexOf(session);
        Sessions.Remove(session);
        session.Dispose();

        // 切換到相鄰 Session
        var next = Sessions.ElementAtOrDefault(Math.Max(0, idx - 1));
        ActiveSession = next;
        SyncQueryTabsFromSession();
    }

    private void SwitchSession(object? param)
    {
        if (param is ConnectionSession session && session != ActiveSession)
        {
            PersistCurrentSessionTabs();
            ActiveSession = session;
            StatusText = session.IsConnected ? $"已連線：{session.DisplayName}" : "未連線";
        }
    }

    // ══════════════════════════════════════════════════════════
    // 連線
    // ══════════════════════════════════════════════════════════

    public async Task ConnectWithProfileAsync(ConnectionProfile profile)
    {
        // 若 ActiveSession 已連線，建立新 Session
        if (ActiveSession?.IsConnected == true)
            AddNewSession();

        var session = ActiveSession!;
        session.IsBusy = true;
        StatusText = $"連線中 {profile.Host}...";
        try
        {
            await session.ConnectionService.ConnectAsync(profile);
            session.IsConnected   = true;
            session.DisplayName   = profile.Name;
            session.ConnectedHost = $"{profile.Host}:{profile.Port}";
            session.ConnectedUser = profile.Username;
            StatusText = $"已連線：{profile.Name}";

            var vr = await session.ConnectionService.ExecuteQueryAsync("SELECT VERSION() AS v;");
            if (vr.Success && vr.Data?.Rows.Count > 0)
                session.ServerVersion = "MySQL " + (vr.Data.Rows[0][0]?.ToString()?.Split('-')[0] ?? "");

            _settingsService.SaveProfile(profile);
            await RefreshTreeAsync();
            AddNewQueryTab();
        }
        catch (Exception ex)
        {
            session.IsConnected = false;
            StatusText = "連線失敗";
            MessageBox.Show($"無法連線：\n{ex.Message}", "連線錯誤",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { session.IsBusy = false; }
    }

    private Task OpenConnectionDialogAsync()
    {
        var dlg = new Views.ConnectionDialog();
        dlg.ShowDialog();
        return Task.CompletedTask;
    }

    private async Task DisconnectCurrentAsync()
    {
        if (ActiveSession == null) return;
        await ActiveSession.ConnectionService.DisconnectAsync();
        ActiveSession.IsConnected   = false;
        ActiveSession.DisplayName   = "未連線";
        ActiveSession.ServerVersion = "";
        ActiveSession.ConnectedHost = "";
        ActiveSession.ConnectedUser = "";
        ActiveSession.SelectedDatabase = null;
        ActiveSession.TreeNodes.Clear();
        QueryTabs.Clear();
        ActiveTab = null;
        StatusText = "已中斷連線";
    }

    // ══════════════════════════════════════════════════════════
    // 樹狀結構
    // ══════════════════════════════════════════════════════════

    public async Task RefreshTreeAsync()
    {
        if (ActiveSession?.IsConnected != true) return;
        var session = ActiveSession;
        session.IsBusy = true;
        try
        {
            session.TreeNodes.Clear();
            var serverNode = new DbTreeNode
            {
                Name = session.ConnectionService.CurrentProfile?.Name ?? "Server",
                NodeType = DbNodeType.Server,
                IsExpanded = true
            };

            var databases = await session.ConnectionService.GetDatabasesAsync();
            foreach (var db in databases)
            {
                var dbNode       = new DbTreeNode { Name = db, NodeType = DbNodeType.Database };
                var tablesFolder = new DbTreeNode { Name = "Tables", NodeType = DbNodeType.TablesFolder, ParentDatabase = db };
                var viewsFolder  = new DbTreeNode { Name = "Views",  NodeType = DbNodeType.ViewsFolder,  ParentDatabase = db };
                dbNode.Children.Add(tablesFolder);
                dbNode.Children.Add(viewsFolder);
                serverNode.Children.Add(dbNode);
            }
            session.TreeNodes.Add(serverNode);
        }
        finally { session.IsBusy = false; }
    }

    public async Task LoadTableNodesAsync(DbTreeNode folderNode)
    {
        if (folderNode.ParentDatabase == null || ActiveSession == null) return;
        folderNode.Children.Clear();
        folderNode.IsLoading = true;
        try
        {
            var svc = ActiveSession.ConnectionService;
            if (folderNode.NodeType == DbNodeType.TablesFolder)
            {
                var tables = await svc.GetTablesAsync(folderNode.ParentDatabase);
                foreach (var t in tables)
                    folderNode.Children.Add(new DbTreeNode
                    {
                        Name = t, NodeType = DbNodeType.Table,
                        ParentDatabase = folderNode.ParentDatabase
                    });
            }
            else if (folderNode.NodeType == DbNodeType.ViewsFolder)
            {
                var views = await svc.GetViewsAsync(folderNode.ParentDatabase);
                foreach (var v in views)
                    folderNode.Children.Add(new DbTreeNode
                    {
                        Name = v, NodeType = DbNodeType.View,
                        ParentDatabase = folderNode.ParentDatabase
                    });
            }
        }
        finally { folderNode.IsLoading = false; }
    }

    // ══════════════════════════════════════════════════════════
    // 查詢分頁
    // ══════════════════════════════════════════════════════════

    /// <summary>切換 Session 時同步 QueryTabs（每個 Session 有獨立的 tab 清單）</summary>
    private readonly Dictionary<Guid, List<QueryTabViewModel>> _sessionTabs = new();

    private void SyncQueryTabsFromSession()
    {
        // 儲存舊 Session 的 tabs
        // (ActiveSession 已更新，這裡操作的是新的 session)
        QueryTabs.Clear();
        ActiveTab = null;

        if (ActiveSession == null) return;

        if (_sessionTabs.TryGetValue(ActiveSession.Id, out var tabs))
        {
            foreach (var t in tabs) QueryTabs.Add(t);
            ActiveTab = QueryTabs.LastOrDefault();
        }
    }

    private void PersistCurrentSessionTabs()
    {
        if (ActiveSession == null) return;
        _sessionTabs[ActiveSession.Id] = QueryTabs.ToList();
    }

    private void AddNewQueryTab(object? _ = null)
    {
        if (ActiveSession?.IsConnected != true) return;
        var tab = new QueryTabViewModel(
            $"Query {QueryTabs.Count + 1}",
            ActiveSession.ConnectionService);
        QueryTabs.Add(tab);
        ActiveTab = tab;
        PersistCurrentSessionTabs();
    }

    private void CloseTab(object? param)
    {
        if (param is QueryTabViewModel tab)
        {
            QueryTabs.Remove(tab);
            if (ActiveTab == tab)
                ActiveTab = QueryTabs.Count > 0 ? QueryTabs[^1] : null;
            PersistCurrentSessionTabs();
        }
    }

    public void OpenNewQueryTab(string sql = "-- 在此輸入 SQL 指令\nSELECT 1;", string? database = null)
    {
        if (ActiveSession?.IsConnected != true) return;
        var tab = new QueryTabViewModel("查詢", ActiveSession.ConnectionService)
        {
            SqlText          = sql,
            SelectedDatabase = database ?? ActiveSession.ConnectionService.CurrentProfile?.DefaultDatabase
        };
        QueryTabs.Add(tab);
        ActiveTab = tab;
        PersistCurrentSessionTabs();
    }

    public void OpenTableQuery(string database, string tableName)
    {
        if (ActiveSession?.IsConnected != true) return;
        var tab = new QueryTabViewModel($"{tableName}", ActiveSession.ConnectionService)
        {
            SqlText          = $"SELECT * FROM `{database}`.`{tableName}` LIMIT 1000;",
            SelectedDatabase = database
        };
        QueryTabs.Add(tab);
        ActiveTab = tab;
        PersistCurrentSessionTabs();
    }

    // ══════════════════════════════════════════════════════════
    // 搜尋
    // ══════════════════════════════════════════════════════════

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { SetProperty(ref _searchText, value); PerformSearch(); }
    }

    private bool _isSearching;
    public bool IsSearching { get => _isSearching; set => SetProperty(ref _isSearching, value); }

    private string _searchResultCount = string.Empty;
    public string SearchResultCount { get => _searchResultCount; set => SetProperty(ref _searchResultCount, value); }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    private System.Threading.CancellationTokenSource? _searchCts;

    private void PerformSearch()
    {
        _searchCts?.Cancel();
        SearchResults.Clear();
        var kw = SearchText?.Trim() ?? "";
        if (string.IsNullOrEmpty(kw)) { IsSearching = false; SearchResultCount = ""; return; }
        IsSearching = true;
        SearchResultCount = "搜尋中...";
        _searchCts = new System.Threading.CancellationTokenSource();
        _ = PerformSearchAsync(kw, _searchCts.Token);
    }

    private async Task PerformSearchAsync(string kw, System.Threading.CancellationToken token)
    {
        await Task.Yield();
        if (token.IsCancellationRequested || ActiveSession == null) return;

        var results = new List<SearchResultItem>();
        var svc = ActiveSession.ConnectionService;

        foreach (var serverNode in TreeNodes)
        {
            foreach (var dbNode in serverNode.Children)
            {
                if (token.IsCancellationRequested) return;
                if (dbNode.NodeType != DbNodeType.Database) continue;
                var dbName = dbNode.Name;

                if (dbName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    results.Add(new SearchResultItem { Name = dbName, Path = dbName, NodeType = DbNodeType.Database, Database = dbName });

                List<string> tableNames = new();
                foreach (var folderNode in dbNode.Children)
                {
                    if (folderNode.NodeType == DbNodeType.TablesFolder)
                    {
                        if (folderNode.Children.Count == 0 && !folderNode.IsLoading)
                            await LoadTableNodesAsync(folderNode);
                        tableNames.AddRange(folderNode.Children.Select(n => n.Name));
                    }
                }

                foreach (var tableName in tableNames)
                {
                    if (token.IsCancellationRequested) return;
                    if (tableName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        results.Add(new SearchResultItem { Name = tableName, Path = $"{dbName}  /  {tableName}", NodeType = DbNodeType.Table, Database = dbName, Table = tableName });

                    try
                    {
                        var cols = await svc.GetColumnsAsync(dbName, tableName);
                        foreach (var col in cols)
                        {
                            if (token.IsCancellationRequested) return;
                            if (col.Field.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                col.Type.Contains(kw,  StringComparison.OrdinalIgnoreCase))
                                results.Add(new SearchResultItem
                                {
                                    Name = col.Field,
                                    Path = $"{dbName}  /  {tableName}  /  {col.Field}  ({col.Type})",
                                    NodeType = DbNodeType.Column,
                                    Database = dbName, Table = tableName
                                });
                        }
                    }
                    catch { /* skip */ }
                }
            }
        }

        if (token.IsCancellationRequested) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
            SearchResultCount = results.Count > 0 ? $"找到 {results.Count} 筆結果" : "沒有符合的結果";
        });
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        IsSearching = false;
    }
}
