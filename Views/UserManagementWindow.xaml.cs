using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public class DbPrivilegeDisplay
{
    public string Database      { get; set; } = "";
    public string Table         { get; set; } = "";
    public List<string> Grants  { get; set; } = new();
    public string GrantsDisplay => string.Join(", ", Grants);
}

public partial class UserManagementWindow : Window
{
    private readonly UserManagementService _svc;
    private DbUser? _currentUser;

    public UserManagementWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        var conn = (System.Windows.Application.Current.MainWindow?.DataContext
                    as MySQLManager.ViewModels.MainViewModel)
                   ?.ActiveSession?.ConnectionService ?? App.ConnectionService;
        _svc = new UserManagementService(conn);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshUsersAsync();
        var dbs = await App.ConnectionService.GetDatabasesAsync();
        GrantDbCombo.ItemsSource  = new[] { "*" }.Concat(dbs).ToList();
        GrantDbCombo.SelectedIndex = 0;
        GrantTblCombo.ItemsSource  = new[] { "*" };
        GrantTblCombo.SelectedIndex = 0;
    }

    private async System.Threading.Tasks.Task RefreshUsersAsync()
    {
        var users = await _svc.GetUsersAsync();
        UserList.ItemsSource = users;
    }

    private async void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserList.SelectedItem is not DbUser u) return;
        _currentUser = u;
        UserHeader.Text    = $"👤 {u.FullName}";
        UserSubHeader.Text = u.IsLocked ? "帳號已鎖定" : "帳號正常";
        LockBtn.Content    = u.IsLocked ? "🔓 解鎖帳號" : "🔒 鎖定帳號";
        EmptyHint.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        await RefreshPrivilegesAsync();
    }

    private async System.Threading.Tasks.Task RefreshPrivilegesAsync()
    {
        if (_currentUser == null) return;
        var privs = await _svc.GetUserPrivilegesAsync(_currentUser.Username, _currentUser.Host);
        PrivGrid.ItemsSource = privs.Select(p => new DbPrivilegeDisplay
        {
            Database = p.Database, Table = p.Table, Grants = p.Grants
        }).ToList();
    }

    // ── 新增使用者 ────────────────────────────────────────────
    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddUserDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var r = await _svc.CreateUserAsync(dlg.Username, dlg.Host, dlg.Password);
        if (!r.Success)
        { MessageBox.Show($"建立失敗：{r.ErrorMessage}", "錯誤"); return; }
        await RefreshUsersAsync();
        MessageBox.Show($"使用者 '{dlg.Username}'@'{dlg.Host}' 已建立。", "成功");
    }

    // ── 刪除使用者 ────────────────────────────────────────────
    private async void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null) return;
        if (MessageBox.Show($"確定刪除 {_currentUser.FullName}？此操作不可恢復！",
            "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var r = await _svc.DropUserAsync(_currentUser.Username, _currentUser.Host);
        if (!r.Success) { MessageBox.Show($"刪除失敗：{r.ErrorMessage}", "錯誤"); return; }
        _currentUser = null;
        EditPanel.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Visible;
        await RefreshUsersAsync();
    }

    // ── 改密碼 ────────────────────────────────────────────────
    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null) return;
        var dlg = new ChangePasswordDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var r = await _svc.ChangePasswordAsync(_currentUser.Username, _currentUser.Host, dlg.NewPassword);
        MessageBox.Show(r.Success ? "密碼已更新。" : $"失敗：{r.ErrorMessage}");
    }

    // ── 鎖定/解鎖 ─────────────────────────────────────────────
    private async void ToggleLock_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null) return;
        var r = await _svc.SetLockAsync(_currentUser.Username, _currentUser.Host, !_currentUser.IsLocked);
        if (!r.Success) { MessageBox.Show($"失敗：{r.ErrorMessage}"); return; }
        _currentUser.IsLocked = !_currentUser.IsLocked;
        LockBtn.Content   = _currentUser.IsLocked ? "🔓 解鎖帳號" : "🔒 鎖定帳號";
        UserSubHeader.Text = _currentUser.IsLocked ? "帳號已鎖定" : "帳號正常";
        await RefreshUsersAsync();
    }

    // ── 開啟授權編輯器 ────────────────────────────────────────
    private async void OpenGrantEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null) return;
        var db  = GrantDbCombo.Text?.Trim() ?? "*";
        var tbl = GrantTblCombo.Text?.Trim() ?? "*";
        var dlg = new GrantEditorDialog(_currentUser, db, tbl, _svc) { Owner = this };
        if (dlg.ShowDialog() == true)
            await RefreshPrivilegesAsync();
    }

    // ── 撤銷選取的權限 ────────────────────────────────────────
    private async void RevokeSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null) return;
        if (PrivGrid.SelectedItem is not DbPrivilegeDisplay row) return;
        if (MessageBox.Show($"確定撤銷 {_currentUser.Username} 在 {row.Database}.{row.Table} 的所有權限？",
            "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var r = await _svc.RevokeAllAsync(_currentUser.Username, _currentUser.Host, row.Database, row.Table);
        if (!r.Success) { MessageBox.Show($"失敗：{r.ErrorMessage}"); return; }
        await RefreshPrivilegesAsync();
    }

    // ── 複製 GRANT SQL ────────────────────────────────────────
    private void CopyGrantSql_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser == null || PrivGrid.SelectedItem is not DbPrivilegeDisplay row) return;
        var sql = _svc.GenerateGrantSql(_currentUser.Username, _currentUser.Host,
                    row.Grants, row.Database, row.Table);
        System.Windows.Clipboard.SetText(sql);
        MessageBox.Show("已複製到剪貼簿。", "完成");
    }
}
