using System.Windows;
namespace MySQLManager.Views;
public partial class ChangePasswordDialog : Window
{
    public string NewPassword { get; private set; } = "";
    public ChangePasswordDialog() => InitializeComponent();
    private void Ok_Click(object s, RoutedEventArgs e)
    {
        if (Pwd1.Password != Pwd2.Password)
        { MessageBox.Show("兩次密碼不一致"); return; }
        NewPassword  = Pwd1.Password;
        DialogResult = true;
    }
    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
