using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class CrudGeneratorWindow : Window
{
    public CrudGeneratorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var svc = GetActiveConnectionService();
        if (svc?.IsConnected != true)
        {
            MessageBox.Show("請先建立資料庫連線", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
            return;
        }

        var dbs = await svc.GetDatabasesAsync();
        DatabaseCombo.ItemsSource = dbs;
        if (dbs.Count > 0) DatabaseCombo.SelectedIndex = 0;
    }

    private async void DatabaseCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        var db = DatabaseCombo.SelectedItem?.ToString();
        if (db == null) return;
        var svc = GetActiveConnectionService();
        if (svc == null) return;
        var tables = await svc.GetTablesAsync(db);
        TableCombo.ItemsSource = tables;
        if (tables.Count > 0) TableCombo.SelectedIndex = 0;
    }

    private async void TableCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        var db    = DatabaseCombo.SelectedItem?.ToString();
        var table = TableCombo.SelectedItem?.ToString();
        if (db == null || table == null) return;
        var svc = GetActiveConnectionService();
        if (svc == null) return;
        var cols = await svc.GetColumnsAsync(db, table);
        ColumnList.ItemsSource = cols;
    }

    private void Language_Changed(object sender, RoutedEventArgs e) { /* nothing */ }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var db    = DatabaseCombo.SelectedItem?.ToString();
        var table = TableCombo.SelectedItem?.ToString();
        if (db == null || table == null)
        {
            MessageBox.Show("請選擇資料庫和資料表", "提示");
            return;
        }

        var svc  = GetActiveConnectionService()!;
        var cols = await svc.GetColumnsAsync(db, table);
        var lang = RbCSharp.IsChecked == true     ? CrudLanguage.CSharp
                 : RbPython.IsChecked == true     ? CrudLanguage.Python
                 : RbPhp.IsChecked == true        ? CrudLanguage.PHP
                 :                                  CrudLanguage.TypeScript;

        var code = CrudGeneratorService.Generate(db, table, cols, lang);
        CodeOutput.Text = code;
        HeaderText.Text = $"✅ {table} — {lang}  ({cols.Count} 個欄位)";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CodeOutput.Text))
            Clipboard.SetText(CodeOutput.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CodeOutput.Text)) return;
        var ext = RbCSharp.IsChecked == true     ? "cs"
                : RbPython.IsChecked == true     ? "py"
                : RbPhp.IsChecked == true        ? "php"
                :                                  "ts";
        var table = TableCombo.SelectedItem?.ToString() ?? "crud";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName    = $"{table}.{ext}",
            Filter      = $"程式碼 (*.{ext})|*.{ext}|All Files (*.*)|*.*",
            DefaultExt  = ext
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllText(dlg.FileName, CodeOutput.Text);
    }

    private static ConnectionService? GetActiveConnectionService()
    {
        var mainVm = Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel;
        return mainVm?.ActiveSession?.ConnectionService;
    }
}
