using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MySQLManager.Services;

namespace MySQLManager.Views;

public class PrivItem
{
    public string Name      { get; set; } = "";
    public bool   IsChecked { get; set; }
}

public partial class GrantEditorDialog : Window
{
    private readonly DbUser                _user;
    private readonly string                _db, _tbl;
    private readonly UserManagementService _svc;
    private List<PrivItem>                 _items = new();

    public GrantEditorDialog(DbUser user, string db, string tbl, UserManagementService svc)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _user = user; _db = db; _tbl = tbl; _svc = svc;
    }

    private void Window_Loaded(object s, RoutedEventArgs e)
    {
        TitleBlock.Text = $"授予 {_user.FullName} 於 {_db}.{_tbl} 的權限";
        _items = UserManagementService.AllPrivileges
                 .Select(p => new PrivItem { Name = p }).ToList();
        PrivList.ItemsSource = _items;
    }

    private void SelectAll_Click(object s, RoutedEventArgs e)
    {
        foreach (var i in _items) i.IsChecked = true;
        PrivList.ItemsSource = null;
        PrivList.ItemsSource = _items;
    }
    private void ClearAll_Click(object s, RoutedEventArgs e)
    {
        foreach (var i in _items) i.IsChecked = false;
        PrivList.ItemsSource = null;
        PrivList.ItemsSource = _items;
    }

    private async void Grant_Click(object s, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsChecked).Select(i => i.Name).ToList();
        if (!selected.Any()) { MessageBox.Show("請至少選擇一項權限"); return; }
        var r = await _svc.GrantAsync(_user.Username, _user.Host, selected, _db, _tbl);
        if (!r.Success) { MessageBox.Show($"GRANT 失敗：{r.ErrorMessage}"); return; }
        DialogResult = true;
    }
    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
