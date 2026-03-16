using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MySQLManager.Models;

namespace MySQLManager.Views;

public partial class LockAnalysisWindow : Window
{
    private DispatcherTimer? _timer;
    private List<LockInfo>   _locks = new();
    private bool             _isV8  = false;

    public LockAnalysisWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            App.FitWindowToScreen(this);
            _isV8 = await DetectMySqlVersionAsync();
            await RefreshAsync();
        };
    }

    private async System.Threading.Tasks.Task<bool> DetectMySqlVersionAsync()
    {
        try
        {
            var r = await App.ConnectionService.ExecuteQueryAsync("SELECT VERSION()");
            if (r.Data?.Rows.Count > 0)
            {
                var ver = r.Data.Rows[0][0]?.ToString() ?? "";
                return ver.StartsWith("8.") || ver.StartsWith("9.");
            }
        }
        catch { }
        return false;
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            StatusLabel.Text = "更新中…";
            _locks = _isV8
                ? await App.ConnectionService.GetLocksV8Async()
                : await App.ConnectionService.GetLocksAsync();

            LockGrid.ItemsSource = _locks;

            var waiters  = _locks.Count;
            var blockers = _locks.Select(l => l.BlockingThread).Distinct().Count();
            var maxWait  = _locks.Count > 0 ? _locks.Max(l => l.WaitSeconds) : 0;

            WaitCountLabel.Text  = waiters.ToString();
            BlockCountLabel.Text = blockers.ToString();
            MaxWaitLabel.Text    = maxWait.ToString();

            StatusLabel.Text = _locks.Count == 0
                ? $"✅ 無鎖等待  |  {DateTime.Now:HH:mm:ss}"
                : $"⚠️ {waiters} 個等待  |  {DateTime.Now:HH:mm:ss}";

            FooterLabel.Text = _isV8
                ? "💡 MySQL 8.0+ 模式：使用 performance_schema.data_lock_waits"
                : "💡 MySQL 5.7 模式：使用 information_schema.INNODB_LOCK_WAITS";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ {ex.Message}";
        }
    }

    private void LockGrid_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LockGrid.SelectedItem is LockInfo info)
        {
            WaitingQueryBox.Text  = info.WaitingQuery;
            BlockingQueryBox.Text = info.BlockingQuery;
        }
    }

    private async void Refresh_Click(object s, RoutedEventArgs e)
        => await RefreshAsync();

    private void AutoRefresh_Checked(object s, RoutedEventArgs e)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        AutoRefreshBtn.Content = "⏱ 停止自動更新";
    }

    private void AutoRefresh_Unchecked(object s, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        AutoRefreshBtn.Content = "⏱ 自動更新 (5s)";
    }

    private async void KillWaiting_Click(object s, RoutedEventArgs e)
    {
        if (LockGrid.SelectedItem is not LockInfo info) return;
        var res = MessageBox.Show(
            $"KILL Thread {info.WaitingThread}（等待中）？\n查詢：{info.WaitQueryShort}",
            "確認 KILL", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        await App.ConnectionService.KillProcessAsync(info.WaitingThread.ToString());
        await RefreshAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}
