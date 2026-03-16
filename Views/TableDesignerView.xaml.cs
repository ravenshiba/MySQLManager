using System.Windows;
using System.Windows.Controls;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class TableDesignerView : Window
{
    private TableDesignerViewModel Vm => (TableDesignerViewModel)DataContext;

    public TableDesignerView(string database, Models.TableDesign? existingDesign = null)
    {
        InitializeComponent();
        DataContext = new TableDesignerViewModel(database, existingDesign);
        Vm.CloseRequested += () => Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // 任何欄位變更 → 更新 SQL 預覽
    private void Column_Changed(object sender, RoutedEventArgs e)
        => Vm.RefreshPreview();

    private void Index_Changed(object sender, RoutedEventArgs e)
        => Vm.RefreshPreview();

    private void CopySql_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Vm.PreviewSql))
        {
            Clipboard.SetText(Vm.PreviewSql);
            MessageBox.Show("SQL 已複製到剪貼簿！", "複製成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
