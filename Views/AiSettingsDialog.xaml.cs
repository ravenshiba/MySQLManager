using System.Windows;
using System.Windows.Controls;

namespace MySQLManager.Views;

public partial class AiSettingsDialog : Window
{
    public AiSettingsDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        // 載入目前設定
        ApiKeyBox.Password = App.AiSqlService.ApiKey;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        App.AiSqlService.ApiKey = ApiKeyBox.Password;
        // 持久化到設定
        App.SettingsService.SaveAiApiKey(ApiKeyBox.Password);
        Close();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        TestStatus.Text = "測試中...";
        TestStatus.Foreground = System.Windows.Media.Brushes.Gray;

        var tempKey = App.AiSqlService.ApiKey;
        App.AiSqlService.ApiKey = ApiKeyBox.Password;

        var result = await App.AiSqlService.GenerateSqlAsync(
            "SELECT 1", null, null);

        App.AiSqlService.ApiKey = tempKey;

        if (result.Success || result.Sql?.Contains("SELECT") == true)
        {
            TestStatus.Text = "✅ 連線成功！";
            TestStatus.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            TestStatus.Text = $"❌ {result.Error}";
            TestStatus.Foreground = System.Windows.Media.Brushes.Red;
        }
    }
}
