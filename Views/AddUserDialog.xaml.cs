using System.Windows;
namespace MySQLManager.Views;
public partial class AddUserDialog : Window
{
    public string Username { get; private set; } = "";
    public string Host     { get; private set; } = "%";
    public string Password { get; private set; } = "";
    public AddUserDialog() => InitializeComponent();
    private void Ok_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text))
        { MessageBox.Show("請輸入使用者名稱"); return; }
        Username = UserBox.Text.Trim();
        Host     = HostBox.Text.Trim();
        Password = PwdBox.Password;
        DialogResult = true;
    }
    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
