using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MySQLManager.ViewModels;
using DbModels = MySQLManager.Models;

namespace MySQLManager.Views;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;
    private DbModels.DbTreeNode? _rightClickedNode;

    public MainWindow()
    {
        InitializeComponent();
        // 啟動時依螢幕大小調整視窗
        Loaded += (_, _) => FitToScreen();
    }

    // ── 搜尋 ──────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Vm.ClearSearch();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        Vm.ClearSearch();
    }

    private void SearchResult_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultList.SelectedItem is not DbModels.SearchResultItem item) return;

        switch (item.NodeType)
        {
            case DbModels.DbNodeType.Table:
            case DbModels.DbNodeType.View:
                if (item.Database != null && item.Table != null)
                    Vm.OpenTableQuery(item.Database, item.Table);
                break;
            case DbModels.DbNodeType.Column:
                if (item.Database != null && item.Table != null)
                    Vm.OpenTableQuery(item.Database, item.Table);
                break;
        }
    }

    // ── 樹狀結構事件 ──────────────────────────────────────────

    private async void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is DbModels.DbTreeNode node)
        {
            if ((node.NodeType == DbModels.DbNodeType.TablesFolder ||
                 node.NodeType == DbModels.DbNodeType.ViewsFolder) &&
                node.Children.Count == 0 && !node.IsLoading)
            {
                await Vm.LoadTableNodesAsync(node);
            }
        }
    }

    private void TreeItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem tvi && tvi.DataContext is DbModels.DbTreeNode node)
        {
            _rightClickedNode = node;
            tvi.IsSelected = true;
            e.Handled = true;
        }
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DbModels.DbTreeNode node)
            _rightClickedNode = node;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        var node = _rightClickedNode;

        foreach (var item in cm.Items)
        {
            if (item is not MenuItem mi) continue;
            var tag = mi.Tag?.ToString();
            mi.Visibility = tag switch
            {
                "CreateTable" => node?.NodeType is DbModels.DbNodeType.Database or DbModels.DbNodeType.TablesFolder
                                 ? Visibility.Visible : Visibility.Collapsed,
                "Refresh"     => Visibility.Visible,
                "DesignTable" => node?.NodeType == DbModels.DbNodeType.Table ? Visibility.Visible : Visibility.Collapsed,
                "QueryTable"  => node?.NodeType == DbModels.DbNodeType.Table ? Visibility.Visible : Visibility.Collapsed,
                "DropTable"   => node?.NodeType == DbModels.DbNodeType.Table ? Visibility.Visible : Visibility.Collapsed,
                "ShowErd"     => node?.NodeType is DbModels.DbNodeType.Database or
                                                   DbModels.DbNodeType.TablesFolder or
                                                   DbModels.DbNodeType.Table
                                 ? Visibility.Visible : Visibility.Collapsed,
                "SearchData"  => node?.NodeType == DbModels.DbNodeType.Table ? Visibility.Visible : Visibility.Collapsed,
                _             => Visibility.Visible
            };
        }
    }

    private async void ContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var node = _rightClickedNode;
        if (node == null) return;

        switch (mi.Tag?.ToString())
        {
            case "EventScheduler":
            {
                var db = node.NodeType == DbModels.DbNodeType.Database ? node.Name : node.ParentDatabase ?? "";
                if (string.IsNullOrEmpty(db)) return;
                new EventSchedulerWindow(db) { Owner = this }.Show();
                break;
            }
            case "CreateTable":
            {
                var db = node.NodeType == DbModels.DbNodeType.Database ? node.Name : node.ParentDatabase ?? "";
                if (string.IsNullOrEmpty(db)) return;
                var designer = new TableDesignerView(db) { Owner = this };
                designer.ShowDialog();
                await Vm.RefreshTreeAsync();
                break;
            }
            case "DesignTable":
            {
                if (node.ParentDatabase == null) return;
                await OpenTableDesignerAsync(node.ParentDatabase, node.Name);
                break;
            }
            case "QueryTable":
            {
                if (node.ParentDatabase == null) return;
                Vm.OpenTableQuery(node.ParentDatabase, node.Name);
                break;
            }
            case "ShowErd":
            {
                // 決定要顯示哪個資料庫的 ERD
                string? erdDb = node.NodeType switch
                {
                    DbModels.DbNodeType.Database     => node.Name,
                    DbModels.DbNodeType.TablesFolder => node.ParentDatabase,
                    DbModels.DbNodeType.Table        => node.ParentDatabase,
                    _                                => null
                };
                if (erdDb == null) return;
                var erd = new ErdWindow(erdDb) { Owner = this };
                erd.Show();
                break;
            }
            case "SearchData":
            {
                var db  = node.NodeType == DbModels.DbNodeType.Table ? node.ParentDatabase : node.Name;
                var tbl = node.NodeType == DbModels.DbNodeType.Table ? node.Name : null;
                new TableSearchWindow(db, tbl) { Owner = this }.Show();
                break;
            }
            case "DataDiff":
            {
                var db  = node.NodeType == DbModels.DbNodeType.Table ? node.ParentDatabase : node.Name;
                var tbl = node.NodeType == DbModels.DbNodeType.Table ? node.Name : null;
                var win = new DataDiffWindow { Owner = this };
                win.Show();
                // 預填左側資料表（LoadDatabasesAsync 在 Loaded 事件中呼叫，這裡等它完成後設定）
                if (db != null)
                {
                    await win.DiffVm.LoadDatabasesAsync();
                    win.DiffVm.LeftDatabase = db;
                    if (tbl != null) win.DiffVm.LeftTable = tbl;
                }
                break;
            }
            case "Refresh":
                await Vm.RefreshTreeAsync();
                break;
            case "DropTable":
            {
                if (node.ParentDatabase == null) return;
                var confirm = MessageBox.Show(
                    $"確定要刪除資料表 `{node.ParentDatabase}`.`{node.Name}` 嗎？\n此操作無法復原！",
                    "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                {
                    if (Vm.ActiveSession?.ConnectionService == null) break;
                    var result = await Vm.ActiveSession.ConnectionService.DropTableAsync(node.ParentDatabase, node.Name);
                    if (result.Success)
                    {
                        MessageBox.Show($"資料表 `{node.Name}` 已刪除。", "完成",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await Vm.RefreshTreeAsync();
                    }
                    else
                    {
                        MessageBox.Show($"刪除失敗：{result.ErrorMessage}", "錯誤",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                break;
            }
            case "TableStats":
            {
                var db = node.NodeType == DbModels.DbNodeType.Database ? node.Name : node.ParentDatabase;
                if (db == null) return;
                new TableStatsWindow { Owner = this }.Show();
                break;
            }
            case "CrudTable":
            {
                if (node.NodeType != DbModels.DbNodeType.Table || node.ParentDatabase == null) return;
                var win = new CrudGeneratorWindow { Owner = this };
                win.Show();
                break;
            }
        }
    }

    // ── 雙擊資料表：快速 SELECT ───────────────────────────────

    private void TreeItem_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem tvi) return;
        if (tvi.DataContext is not DbModels.DbTreeNode node) return;
        if (node.NodeType != DbModels.DbNodeType.Table) return;
        if (node.ParentDatabase == null) return;

        e.Handled = true;
        // 開啟新查詢 Tab，預填 SELECT *
        var sql = $"SELECT *\nFROM `{node.ParentDatabase}`.`{node.Name}`\nLIMIT 1000;";
        Vm.OpenNewQueryTab(sql, node.ParentDatabase);
    }

    private async Task OpenTableDesignerAsync(string database, string tableName)
    {
        if (Vm.ActiveSession?.ConnectionService == null) return;
        var cols   = await Vm.ActiveSession.ConnectionService.GetColumnsAsync(database, tableName);
        var design = new DbModels.TableDesign
        {
            Database = database, TableName = tableName, IsNewTable = false
        };

        foreach (var col in cols)
        {
            var (typeName, length, decimals) = ParseTypeString(col.Type);
            design.Columns.Add(new DbModels.ColumnDefinition
            {
                Name = col.Field, OriginalName = col.Field,
                DataType = typeName.ToUpper(), Length = length, Decimals = decimals,
                IsNullable = col.Null == "YES", IsPrimaryKey = col.Key == "PRI",
                IsAutoIncrement = col.Extra.Contains("auto_increment"),
                IsUnsigned = col.Type.Contains("unsigned"),
                DefaultValue = col.Default, IsNew = false
            });
        }

        var designer = new TableDesignerView(database, design) { Owner = this };
        designer.ShowDialog();
    }

    private static (string type, int? length, int? decimals) ParseTypeString(string raw)
    {
        raw = raw.ToLower().Replace("unsigned", "").Trim();
        var parenStart = raw.IndexOf('(');
        if (parenStart < 0) return (raw, null, null);
        var typeName = raw[..parenStart].Trim();
        var inner    = raw[(parenStart + 1)..raw.IndexOf(')')].Trim();
        if (inner.Contains(','))
        {
            var parts = inner.Split(',');
            return (typeName,
                int.TryParse(parts[0].Trim(), out var l) ? l : null,
                int.TryParse(parts[1].Trim(), out var d) ? d : null);
        }
        return (typeName, int.TryParse(inner, out var len) ? len : null, null);
    }

    private void SchemaCompare_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new SchemaCompareWindow { Owner = this }.Show();
    }

    private void DataDiff_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new DataDiffWindow { Owner = this }.Show();
    }

    private void RoutineEditor_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new RoutineEditorWindow { Owner = this }.Show();
    }

    private void Explain_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 若目前有查詢分頁，帶入當前 SQL
        var vm = DataContext as MySQLManager.ViewModels.MainViewModel;
        var currentSql = vm?.ActiveTab?.SqlText ?? string.Empty;
        var db = vm?.ActiveTab?.SelectedDatabase;
        new ExplainWindow(currentSql, db) { Owner = this }.Show();
    }

    private void CsvImport_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new CsvImportWindow { Owner = this }.ShowDialog();
    }

    private void Monitor_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new MonitorWindow { Owner = this }.Show();
    }

    private void TableSearch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var vm = DataContext as MySQLManager.ViewModels.MainViewModel;
        new TableSearchWindow(vm?.SelectedDatabase) { Owner = this }.Show();
    }

    private void Backup_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new BackupWindow { Owner = this }.ShowDialog();
    }

    private void CrudGenerator_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new CrudGeneratorWindow { Owner = this }.Show();
    }

    private void AuditLog_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new AuditLogWindow { Owner = this }.Show();
    }

    private void SlowQuery_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new SlowQueryWindow { Owner = this }.Show();
    }

    private void TableStats_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new TableStatsWindow { Owner = this }.Show();
    }

    private void LockAnalysis_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        new LockAnalysisWindow { Owner = this }.Show();
    }

    private void MainWindow_PreviewKeyDown(object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.OemQuestion &&
            System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            new ShortcutHelpWindow { Owner = this }.ShowDialog();
            e.Handled = true;
        }
    }

    // ── Tab 改名 ──────────────────────────────────────────────

    private void TabLabel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 雙擊才改名
        if (e.ClickCount < 2) return;
        if (sender is not System.Windows.Controls.TextBlock lbl) return;
        var parent = lbl.Parent as System.Windows.Controls.StackPanel;
        if (parent == null) return;
        var editBox = parent.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
        if (editBox == null) return;

        lbl.Visibility     = System.Windows.Visibility.Collapsed;
        editBox.Visibility = System.Windows.Visibility.Visible;
        editBox.SelectAll();
        editBox.Focus();
        e.Handled = true;
    }

    private void TabEditBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        => FinishTabRename(sender as System.Windows.Controls.TextBox);

    private void TabEditBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter ||
            e.Key == System.Windows.Input.Key.Escape)
            FinishTabRename(sender as System.Windows.Controls.TextBox);
    }

    private static void FinishTabRename(System.Windows.Controls.TextBox? box)
    {
        if (box == null) return;
        var parent = box.Parent as System.Windows.Controls.StackPanel;
        if (parent == null) return;
        var lbl = parent.Children.OfType<System.Windows.Controls.TextBlock>()
                        .FirstOrDefault(t => t.Name == "TabLbl");
        if (lbl != null) lbl.Visibility = System.Windows.Visibility.Visible;
        box.Visibility = System.Windows.Visibility.Collapsed;
    }

    // ══════════════════════════════════════════════════════════
    // 新功能按鈕：AI 助手 / 收藏庫 / 排程備份 / 主題
    // ══════════════════════════════════════════════════════════

    private void AiChat_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var win = new AiChatWindow { Owner = this };
        win.CurrentDatabase = Vm?.SelectedDatabase;
        win.InsertSqlRequested += sql =>
        {
            var tab = Vm?.ActiveTab;
            if (tab != null) tab.SqlText = sql;
        };
        win.Show();
    }

    private void Snippets_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var win = new SnippetLibraryWindow { Owner = this };
        win.InsertRequested += sql =>
        {
            var tab = Vm?.ActiveTab;
            if (tab != null) tab.SqlText = (tab.SqlText.TrimEnd() + "\n\n" + sql).TrimStart();
        };
        win.ShowDialog();
    }

    private void ScheduledBackup_Click(object sender, System.Windows.RoutedEventArgs e)
        => new ScheduledBackupWindow { Owner = this }.Show();

    private void ToggleTheme_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        App.ThemeService.Toggle();
        if (ThemeBtnIcon != null)
            ThemeBtnIcon.Text = App.ThemeService.IsDark ? "☀️" : "🌙";
    }

    private void SetAccentBlue_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => App.ThemeService.SetAccent(MySQLManager.Services.AccentTheme.Blue);
    private void SetAccentGreen_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => App.ThemeService.SetAccent(MySQLManager.Services.AccentTheme.Green);
    private void SetAccentPurple_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => App.ThemeService.SetAccent(MySQLManager.Services.AccentTheme.Purple);
    private void SetAccentOrange_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => App.ThemeService.SetAccent(MySQLManager.Services.AccentTheme.Orange);
    private void SetAccentRed_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
        => App.ThemeService.SetAccent(MySQLManager.Services.AccentTheme.Red);

    private void CrossConnect_Click(object s, System.Windows.RoutedEventArgs e)
        => new CrossConnectWindow { Owner = this }.Show();

    private void FitToScreen()
    {
        var screen = SystemParameters.WorkArea;
        // 若視窗比工作區域大，縮小至 90%
        if (Width  > screen.Width)  Width  = screen.Width  * 0.90;
        if (Height > screen.Height) Height = screen.Height * 0.90;
        // 置中
        Left = (screen.Width  - Width)  / 2 + screen.Left;
        Top  = (screen.Height - Height) / 2 + screen.Top;
    }

    private void UserManagement_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession == null)
        { MessageBox.Show("請先建立資料庫連線", "未連線"); return; }
        new UserManagementWindow { Owner = this }.ShowDialog();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession == null)
        { MessageBox.Show("請先建立資料庫連線", "未連線"); return; }
        new DashboardWindow { Owner = this }.Show();
    }

    private void SqlFormatOptions_Click(object sender, RoutedEventArgs e)
        => new SqlFormatOptionsDialog { Owner = this }.ShowDialog();

    // ── 語言切換 ──────────────────────────────────────────────────────────
    private void LangZh_Click(object s, System.Windows.RoutedEventArgs e)
        => SetLanguage(MySQLManager.Services.AppLanguage.ZhTW);
    private void LangEn_Click(object s, System.Windows.RoutedEventArgs e)
        => SetLanguage(MySQLManager.Services.AppLanguage.En);
    private void LangJa_Click(object s, System.Windows.RoutedEventArgs e)
        => SetLanguage(MySQLManager.Services.AppLanguage.Ja);

    private void SetLanguage(MySQLManager.Services.AppLanguage lang)
    {
        App.LocalizationService.SetLanguage(lang);
        // Highlight active language button
        LangZhBtn.FontWeight = lang == MySQLManager.Services.AppLanguage.ZhTW
            ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
        LangEnBtn.FontWeight = lang == MySQLManager.Services.AppLanguage.En
            ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
        LangJaBtn.FontWeight = lang == MySQLManager.Services.AppLanguage.Ja
            ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
        LangZhBtn.Foreground = lang == MySQLManager.Services.AppLanguage.ZhTW
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25,118,210))
            : System.Windows.SystemColors.ControlTextBrush;
        LangEnBtn.Foreground = lang == MySQLManager.Services.AppLanguage.En
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25,118,210))
            : System.Windows.SystemColors.ControlTextBrush;
        LangJaBtn.Foreground = lang == MySQLManager.Services.AppLanguage.Ja
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(25,118,210))
            : System.Windows.SystemColors.ControlTextBrush;
    }

}
