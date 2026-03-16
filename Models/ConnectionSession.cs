using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MySQLManager.Services;

namespace MySQLManager.Models;

/// <summary>
/// 代表一個獨立的 MySQL 連線工作階段
/// 每個 Session 持有自己的 ConnectionService、TreeNodes、QueryTabs
/// </summary>
public class ConnectionSession : INotifyPropertyChanged, IDisposable
{
    // ── 基本屬性 ──────────────────────────────────────────────

    public Guid Id { get; } = Guid.NewGuid();

    private string _displayName = "未連線";
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDot)); OnPropertyChanged(nameof(TabTitle)); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    private string _serverVersion = "";
    public string ServerVersion
    {
        get => _serverVersion;
        set { _serverVersion = value; OnPropertyChanged(); }
    }

    private string _connectedHost = "";
    public string ConnectedHost
    {
        get => _connectedHost;
        set { _connectedHost = value; OnPropertyChanged(); }
    }

    private string _connectedUser = "";
    public string ConnectedUser
    {
        get => _connectedUser;
        set { _connectedUser = value; OnPropertyChanged(); }
    }

    private string? _selectedDatabase;
    public string? SelectedDatabase
    {
        get => _selectedDatabase;
        set { _selectedDatabase = value; OnPropertyChanged(); }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    // 側邊欄 Tab 顯示文字（截短）
    public string TabTitle => IsConnected
        ? (DisplayName.Length > 16 ? DisplayName[..16] + "…" : DisplayName)
        : "未連線";

    // 連線狀態指示器顏色
    public string StatusDot => IsConnected ? "🟢" : "⚪";

    // ── 服務與資料 ────────────────────────────────────────────

    public ConnectionService ConnectionService { get; } = new ConnectionService();
    public ObservableCollection<DbTreeNode> TreeNodes { get; } = new();

    // ── INotifyPropertyChanged ────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Dispose ───────────────────────────────────────────────

    public void Dispose()
    {
        ConnectionService.Dispose();
    }
}
