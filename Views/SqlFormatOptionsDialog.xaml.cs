using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class SqlFormatOptionsDialog : Window
{
    public SqlFormatOptionsDialog()
    {
        InitializeComponent();
        var opts = SqlFormatOptions.Current;
        UpperCaseChk.IsChecked   = opts.UppercaseKeywords;
        NewlineAndChk.IsChecked  = opts.NewlineBeforeAnd;
        CompactJoinsChk.IsChecked= opts.CompactJoins;
        foreach (ComboBoxItem item in IndentCombo.Items)
            if (item.Tag?.ToString() == opts.IndentSize.ToString())
            { IndentCombo.SelectedItem = item; break; }
    }

    private void Save_Click(object s, RoutedEventArgs e)
    {
        var opts = new SqlFormatOptions
        {
            UppercaseKeywords = UpperCaseChk.IsChecked == true,
            NewlineBeforeAnd  = NewlineAndChk.IsChecked  == true,
            CompactJoins      = CompactJoinsChk.IsChecked == true,
            IndentSize        = int.TryParse(
                (IndentCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var i) ? i : 4
        };
        opts.Save();
        DialogResult = true;
    }
    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
