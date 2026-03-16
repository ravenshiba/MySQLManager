using System.Windows;
using System.Windows.Controls;
using MySQLManager.Models;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class ConnectionDialog : Window
{
    private ConnectionDialogViewModel Vm => (ConnectionDialogViewModel)DataContext;

    public ConnectionDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        Vm.CloseRequested += () => Close();
    }

    private void PasswordInput_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectionDialogViewModel vm)
            vm.EditingProfile.Password = ((PasswordBox)sender).Password;
    }

    private void ProfileItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ConnectionProfile p })
            Vm.SelectProfileCommand.Execute(p);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SshPasswordBox_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ConnectionDialogViewModel vm && vm.EditingProfile != null)
            vm.EditingProfile.SshPassword = SshPasswordBox.Password;
    }

    private void BrowseSshKey_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇 SSH 私鑰檔",
            Filter = "私鑰檔 (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|所有檔案 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true &&
            DataContext is ConnectionDialogViewModel vm && vm.EditingProfile != null)
            vm.EditingProfile.SshKeyPath = dlg.FileName;
    }
    private void BrowseCaCert_Click(object s, System.Windows.RoutedEventArgs e)
        => BrowsePemFile(v => Vm.EditingProfile.SslCaCert = v);

    private void BrowseClientCert_Click(object s, System.Windows.RoutedEventArgs e)
        => BrowsePemFile(v => Vm.EditingProfile.SslClientCert = v);

    private void BrowseClientKey_Click(object s, System.Windows.RoutedEventArgs e)
        => BrowsePemFile(v => Vm.EditingProfile.SslClientKey = v);

    private static void BrowsePemFile(Action<string> setter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇憑證檔案",
            Filter = "憑證/金鑰檔案 (*.pem;*.crt;*.key;*.cer)|*.pem;*.crt;*.key;*.cer|所有檔案 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) setter(dlg.FileName);
    }
}
