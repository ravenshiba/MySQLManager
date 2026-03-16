using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

// ── Data models ───────────────────────────────────────────────────────────
public class ServerStatus
{
    public long   ThreadsConnected  { get; set; }
    public long   MaxConnections    { get; set; }
    public long   ThreadsRunning    { get; set; }
    public long   QueriesTotal      { get; set; }
    public long   ComSelect         { get; set; }
    public long   ComInsert         { get; set; }
    public long   ComUpdate         { get; set; }
    public long   ComDelete         { get; set; }
    public long   BytesSent         { get; set; }
    public long   BytesReceived     { get; set; }
    public long   Uptime            { get; set; }
    public long   SlowQueries       { get; set; }
    public long   OpenTables        { get; set; }
    public long   SelectFullJoin    { get; set; }

    public string BytesSentLabel     => FormatBytes(BytesSent);
    public string BytesReceivedLabel => FormatBytes(BytesReceived);
    public string UptimeLabel        => TimeSpan.FromSeconds(Uptime).ToString(@"d\d\ hh\:mm\:ss");

    public ObservableCollection<ProcessInfo> Processes { get; set; } = new();

    private static string FormatBytes(long b)
        => b < 1024 ? $"{b} B"
         : b < 1024 * 1024 ? $"{b / 1024.0:F1} KB"
         : b < 1024L * 1024 * 1024 ? $"{b / (1024.0 * 1024):F1} MB"
         : $"{b / (1024.0 * 1024 * 1024):F2} GB";
}

// ── ViewModel ─────────────────────────────────────────────────────────────
public partial class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly ConnectionService _conn;
    private DispatcherTimer?           _timer;
    private bool                       _disposed;

    [ObservableProperty] private ServerStatus  _status      = new();
    [ObservableProperty] private string        _lastUpdated = "—";
    [ObservableProperty] private string        _statusText  = "就緒";
    [ObservableProperty] private double        _connectionUsagePct;
    [ObservableProperty] private string        _fullJoinColor = "#4CAF50";
    [ObservableProperty] private string        _fullJoinLabel = "0";
    [ObservableProperty] private List<int>     _intervals     = new() { 2, 5, 10, 30 };
    [ObservableProperty] private int           _selectedInterval = 5;
    [ObservableProperty] private bool          _isRunning;

    public string AutoLabel => IsRunning ? "⏹ 停止更新" : "▶ 啟動自動更新";

    public MonitorViewModel(ConnectionService conn)
    {
        _conn = conn;
    }

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            StatusText = "更新中…";
            var r = await _conn.ExecuteQueryAsync(@"
                SHOW GLOBAL STATUS WHERE Variable_name IN (
                    'Threads_connected','Max_used_connections','Threads_running',
                    'Queries','Com_select','Com_insert','Com_update','Com_delete',
                    'Bytes_sent','Bytes_received','Uptime','Slow_queries',
                    'Open_tables','Select_full_join');
                SHOW PROCESSLIST;");

            var s = new ServerStatus();
            if (r.Data != null)
            {
                foreach (System.Data.DataRow row in r.Data.Rows)
                {
                    var name = row[0]?.ToString() ?? "";
                    if (!long.TryParse(row[1]?.ToString(), out var val)) continue;
                    switch (name)
                    {
                        case "Threads_connected":  s.ThreadsConnected  = val; break;
                        case "Max_used_connections": s.MaxConnections   = val; break;
                        case "Threads_running":    s.ThreadsRunning    = val; break;
                        case "Queries":            s.QueriesTotal      = val; break;
                        case "Com_select":         s.ComSelect         = val; break;
                        case "Com_insert":         s.ComInsert         = val; break;
                        case "Com_update":         s.ComUpdate         = val; break;
                        case "Com_delete":         s.ComDelete         = val; break;
                        case "Bytes_sent":         s.BytesSent         = val; break;
                        case "Bytes_received":     s.BytesReceived     = val; break;
                        case "Uptime":             s.Uptime            = val; break;
                        case "Slow_queries":       s.SlowQueries       = val; break;
                        case "Open_tables":        s.OpenTables        = val; break;
                        case "Select_full_join":   s.SelectFullJoin    = val; break;
                    }
                }
            }

            // Load processes from second result
            var pr = await _conn.ExecuteQueryAsync("SHOW PROCESSLIST");
            if (pr.Data != null)
            {
                s.Processes.Clear();
                foreach (System.Data.DataRow row in pr.Data.Rows)
                {
                    s.Processes.Add(new ProcessInfo
                    {
                        Id      = row["Id"]?.ToString()      ?? "",
                        User    = row["User"]?.ToString()    ?? "",
                        Host    = row["Host"]?.ToString()    ?? "",
                        Db      = row["db"]?.ToString()      ?? "",
                        Command = row["Command"]?.ToString() ?? "",
                        Time    = row["Time"]?.ToString()    ?? "",
                        State   = row["State"]?.ToString()   ?? "",
                        Info    = row["Info"]?.ToString()    ?? "",
                    });
                }
            }

            var maxConn = s.MaxConnections > 0 ? s.MaxConnections : 100;
            ConnectionUsagePct = Math.Min(100, s.ThreadsConnected * 100.0 / maxConn);
            FullJoinLabel = s.SelectFullJoin.ToString();
            FullJoinColor = s.SelectFullJoin > 0 ? "#E53935" : "#4CAF50";
            Status      = s;
            LastUpdated = DateTime.Now.ToString("HH:mm:ss");
            StatusText  = $"✅ {LastUpdated}";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ {ex.Message}";
        }
    }

    public void StartAuto()
    {
        IsRunning = true;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SelectedInterval)
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        OnPropertyChanged(nameof(AutoLabel));
    }

    public void StopAuto()
    {
        _timer?.Stop();
        _timer = null;
        IsRunning = false;
        OnPropertyChanged(nameof(AutoLabel));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAuto();
    }
}
