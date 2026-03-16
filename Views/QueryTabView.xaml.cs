using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using MySQLManager.ViewModels;
using MySQLManager.Helpers;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using System.Collections.Generic;

namespace MySQLManager.Views;

[SupportedOSPlatform("windows")]
public partial class QueryTabView : UserControl
{
    private QueryTabViewModel Vm => (QueryTabViewModel)DataContext;
    private bool _suppressTextChange;
    private FoldingManager? _foldingManager;

    public QueryTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FindReplace.Attach(SqlEditor);
        // Save column widths when user resizes them
        ResultGrid.ColumnReordered += (_, __) => SaveCurrentColumnWidths();
        // Enable code folding
        _foldingManager = FoldingManager.Install(SqlEditor.TextArea);
        // 初始化分頁下拉選單選中 500
        foreach (ComboBoxItem item in PageSizeCombo.Items)
            if (item.Tag?.ToString() == "500") { PageSizeCombo.SelectedItem = item; break; }
        if (DataContext is not QueryTabViewModel vm) return;
        // Ctrl+Shift+S → 開啟收藏庫
        this.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.S &&
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift))
                    == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                OpenSnippetLibrary();
                ke.Handled = true;
            }
        };

        // 雙向同步 SQL 文字
        SqlEditor.Text = vm.SqlText;
        SqlEditor.TextChanged += async (_, _) =>
        {
            if (_suppressTextChange) return;
            vm.SqlText = SqlEditor.Text;
            // 觸發自動完成
            var offset = SqlEditor.CaretOffset;
            var textBefore = SqlEditor.Text[..Math.Min(offset, SqlEditor.Text.Length)];
            await vm.UpdateCompletionsAsync(textBefore);
        };

        // 接受自動完成文字插入
        vm.InsertTextRequested += InsertCompletion;
        // 開啟歷史視窗
        vm.OpenHistoryRequested += OpenHistory;
        // 格式化
        vm.SqlFormatRequested += sql =>
        {
            _suppressTextChange = true;
            SqlEditor.Text = sql;
            _suppressTextChange = false;
        };

        TrySetSyntaxHighlighting();
    }

    private void TrySetSyntaxHighlighting()
    {
        try
        {
            // 載入自定義 MySQL 語法高亮（從嵌入資源）
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            const string resourceName = "MySQLManager.Resources.Highlighting.MySQL.xshd";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new System.Xml.XmlTextReader(stream);
                SqlEditor.SyntaxHighlighting =
                    ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader
                        .Load(reader, ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
            }
            else
            {
                // Fallback: 用內建 SQL 定義
                SqlEditor.SyntaxHighlighting =
                    ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
                        .GetDefinition("SQL");
            }

            // 設定編輯器背景（依主題切換）
            ApplyEditorTheme();
            App.ThemeService.ThemeChanged += _ => ApplyEditorTheme();
        }
        catch { }
    }

    // ── 插入自動完成文字 ──────────────────────────────────────

    private void InsertCompletion(string text)
    {
        _suppressTextChange = true;
        try
        {
            // 取代游標前的當前詞
            var offset = SqlEditor.CaretOffset;
            var doc    = SqlEditor.Document;
            var line   = doc.GetLineByOffset(offset);
            var lineText = doc.GetText(line.Offset, offset - line.Offset);
            var wordStart = offset;
            while (wordStart > line.Offset &&
                   (char.IsLetterOrDigit(doc.GetCharAt(wordStart - 1)) ||
                    doc.GetCharAt(wordStart - 1) == '_' ||
                    doc.GetCharAt(wordStart - 1) == '.'))
                wordStart--;

            doc.Replace(wordStart, offset - wordStart, text);
            SqlEditor.CaretOffset = wordStart + text.Length;
        }
        finally
        {
            _suppressTextChange = false;
            Vm.SqlText = SqlEditor.Text;
        }
    }

    // ── 歷史視窗 ──────────────────────────────────────────────

    private void OpenHistory()
    {
        var win = new QueryHistoryWindow
        {
            Owner = Window.GetWindow(this)
        };
        if (win.ShowDialog() == true && win.ChosenSql != null)
        {
            _suppressTextChange = true;
            SqlEditor.Text = win.ChosenSql;
            _suppressTextChange = false;
            if (win.ChosenDatabase != null)
                Vm.SelectedDatabase = win.ChosenDatabase;
            if (win.ShouldRun)
                Vm.RunQueryCommand.Execute(null);
        }
    }

    // ── 鍵盤快捷鍵 ───────────────────────────────────────────

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 自動完成下拉開啟時，方向鍵/Enter/Esc 由下拉處理
        if (Vm?.ShowCompletions == true)
        {
            if (e.Key == Key.Down)
            {
                CompletionList.Focus();
                if (CompletionList.Items.Count > 0)
                    CompletionList.SelectedIndex = Math.Max(0, CompletionList.SelectedIndex);
                e.Handled = true; return;
            }
            if (e.Key == Key.Escape)
            {
                Vm.ShowCompletions = false;
                e.Handled = true; return;
            }
            if (e.Key == Key.Tab || e.Key == Key.Enter)
            {
                if (Vm.SelectedCompletion != null)
                    Vm.AcceptCompletion(Vm.SelectedCompletion);
                e.Handled = true; return;
            }
        }

        // F5：執行
        if (e.Key == Key.F5 && Vm?.RunQueryCommand.CanExecute(null) == true)
        {
            Vm.RunQueryCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+H：歷史
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenHistory();
            e.Handled = true;
        }
        // Ctrl+S：儲存
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control
            && Vm?.SaveChangesCommand.CanExecute(null) == true)
        {
            Vm.SaveChangesCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Space：強制觸發自動完成
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var offset = SqlEditor.CaretOffset;
            var text   = SqlEditor.Text[..Math.Min(offset, SqlEditor.Text.Length)];
            _ = Vm?.UpdateCompletionsAsync(text);
            e.Handled = true;
        }
        // Ctrl+Shift+F：格式化
        if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Vm?.FormatSqlCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+O：開啟 SQL 檔案
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenSqlFile();
            e.Handled = true;
        }
        // Ctrl+Shift+O：另存 SQL 檔案
        if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveSqlFile();
            e.Handled = true;
        }
        // Ctrl+Tab / Ctrl+Shift+Tab：切換結果子 Tab
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var vm = Vm;
            if (vm != null && vm.MultiResults.Count > 1)
            {
                int cur = vm.MultiResults.IndexOf(vm.ActiveResult!);
                int next = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? (cur - 1 + vm.MultiResults.Count) % vm.MultiResults.Count
                    : (cur + 1) % vm.MultiResults.Count;
                vm.SelectResultCommand.Execute(vm.MultiResults[next]);
                e.Handled = true;
            }
        }
    }

    // ── 自動完成下拉操作 ──────────────────────────────────────

    private void CompletionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            if (Vm?.SelectedCompletion != null)
                Vm.AcceptCompletion(Vm.SelectedCompletion);
            SqlEditor.Focus();
            e.Handled = true;
        }
        if (e.Key == Key.Escape)
        {
            if (Vm != null) Vm.ShowCompletions = false;
            SqlEditor.Focus();
            e.Handled = true;
        }
    }

    private void CompletionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.SelectedCompletion != null)
        {
            Vm.AcceptCompletion(Vm.SelectedCompletion);
            SqlEditor.Focus();
        }
    }

    // ── AI 面板 ───────────────────────────────────────────────

    private void AiPanel_Toggle(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.ShowAiPanel = !Vm.ShowAiPanel;
    }

    // ── 分頁 ──────────────────────────────────────────────────

    private void PageInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;
        CommitPageInput();
        e.Handled = true;
    }

    private void PageInputBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitPageInput();

    private void CommitPageInput()
    {
        if (Vm == null) return;
        if (int.TryParse(PageInputBox.Text, out int p))
            Vm.CurrentPage = p;
        else
            PageInputBox.Text = Vm.CurrentPage.ToString();
    }

    private void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm == null || PageSizeCombo.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag?.ToString(), out int size))
            Vm.PageSize = size;
    }

    // ── SQL 片段收藏庫 ──────────────────────────────────────
    private void OpenSnippets_Click(object sender, RoutedEventArgs e)
        => OpenSnippetLibrary();

    private void OpenSnippetLibrary()
    {
        var win = new SnippetLibraryWindow { Owner = Window.GetWindow(this) };
        win.InsertRequested += sql =>
        {
            var doc = SqlEditor.Document;
            doc.Insert(SqlEditor.CaretOffset, sql);
        };
        win.ShowDialog();
    }

    private void AiSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AiSettingsDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void OpenChart_Click(object sender, RoutedEventArgs e)
    {
        var data = Vm?.ResultData;
        if (data == null || data.Rows.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "請先執行查詢以取得資料", "提示",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        new ChartWindow(data) { Owner = Window.GetWindow(this) }.Show();
    }

    private void AiPromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            Vm?.AiGenerateCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── DataGrid 編輯事件 ─────────────────────────────────────

    private void ResultGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (Vm?.IsEditMode != true) { e.Cancel = true; return; }
        if (e.Row.Item is System.Data.DataRowView drv)
            Vm.OnCellBeginEdit(drv);
    }

    private void ResultGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
            Vm?.OnPropertyChanged("HasPendingChanges");
    }

    // ── 編輯器主題 ───────────────────────────────────────────
    private void ApplyEditorTheme()
    {
        Dispatcher.Invoke(() =>
        {
            bool dark = App.ThemeService.IsDark;
            SqlEditor.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(
                    dark ? (byte)0x2C : (byte)0xFF,
                    dark ? (byte)0x2E : (byte)0xFF,
                    dark ? (byte)0x33 : (byte)0xFF));
            SqlEditor.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(
                    dark ? (byte)0xE8 : (byte)0x1A,
                    dark ? (byte)0xEA : (byte)0x1A,
                    dark ? (byte)0xED : (byte)0x2E));
            SqlEditor.LineNumbersForeground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x9A, 0xA0, 0xA6));
        });
    }

        // ══════════════════════════════════════════════════════════
    // 結果表右鍵選單
    // ══════════════════════════════════════════════════════════

    // 追蹤右鍵點擊的儲存格
    private DataGridCell? _rightClickedCell;
    private string?       _rightClickedColName;
    private string?       _rightClickedValue;

    private void ResultGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        // 找到點擊的 DataGridCell
        var hit = ResultGrid.InputHitTest(e.GetPosition(ResultGrid)) as DependencyObject;
        _rightClickedCell = null;
        _rightClickedColName = null;
        _rightClickedValue   = null;

        while (hit != null)
        {
            if (hit is DataGridCell cell)
            {
                _rightClickedCell = cell;
                // 取欄位名稱
                if (cell.Column?.Header is string h) _rightClickedColName = h;
                // 取儲存格值
                if (cell.DataContext is System.Data.DataRowView drv &&
                    _rightClickedColName != null &&
                    drv.Row.Table.Columns.Contains(_rightClickedColName))
                    _rightClickedValue = drv.Row[_rightClickedColName]?.ToString() ?? "NULL";
                break;
            }
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }
    }

    // ── 複製 ──────────────────────────────────────────────────

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedValue != null)
            Clipboard.SetText(_rightClickedValue);
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedRow is not System.Data.DataRowView drv) return;
        var vals = drv.Row.ItemArray.Select(v => v?.ToString() ?? "");
        Clipboard.SetText(string.Join("\t", vals));
    }

    private void CopyRowCsv_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedRow is not System.Data.DataRowView drv) return;
        var vals = drv.Row.ItemArray.Select(v =>
        {
            var s = v?.ToString() ?? "";
            return s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        });
        Clipboard.SetText(string.Join(",", vals));
    }

    private void CopyColName_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedColName != null)
            Clipboard.SetText(_rightClickedColName);
    }

    // ── 篩選（產生 WHERE 條件插入 SQL 編輯器）────────────────

    private void FilterEqual_Click(object sender, RoutedEventArgs e)
        => InsertWhereClause("=");

    private void FilterNotEqual_Click(object sender, RoutedEventArgs e)
        => InsertWhereClause("!=");

    private void FilterLike_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedColName == null || _rightClickedValue == null) return;
        var clause = $"\n  AND `{_rightClickedColName}` LIKE '%{_rightClickedValue}%'";
        AppendToEditor(clause);
    }

    private void InsertWhereClause(string op)
    {
        if (_rightClickedColName == null || _rightClickedValue == null) return;
        // 數字不加引號
        var val = double.TryParse(_rightClickedValue, out _)
            ? _rightClickedValue
            : $"'{_rightClickedValue.Replace("'", "''")}'";
        var clause = $"\n  AND `{_rightClickedColName}` {op} {val}";
        AppendToEditor(clause);
    }

    private void AppendToEditor(string text)
    {
        var doc = SqlEditor.Document;
        // 若 SQL 含 LIMIT，插在 LIMIT 之前
        var upper = doc.Text.ToUpperInvariant();
        var limitIdx = upper.LastIndexOf("\nLIMIT", StringComparison.Ordinal);
        if (limitIdx >= 0)
            doc.Insert(limitIdx, text);
        else
            doc.Insert(doc.TextLength, text);
    }

    // ── 快速查詢 ──────────────────────────────────────────────

    private void QuickSelect_Click(object sender, RoutedEventArgs e)
    {
        // 用 ActiveResult 的 SQL 推算資料表名稱
        var sql = Vm?.ActiveResult?.Sql ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(
            sql, @"FROM\s+[`]?(\w+)[`]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return;
        var table = match.Groups[1].Value;
        var db    = Vm?.SelectedDatabase;
        var newSql = db != null
            ? $"SELECT * FROM `{db}`.`{table}` LIMIT 500;"
            : $"SELECT * FROM `{table}` LIMIT 500;";
        SqlEditor.Document.Text = newSql;
    }

    private void OpenNewTab_Click(object sender, RoutedEventArgs e)
    {
        var sql = Vm?.ActiveResult?.Sql;
        if (sql == null) return;
        var mainVm = Application.Current.MainWindow?.DataContext
                     as MySQLManager.ViewModels.MainViewModel;
        mainVm?.OpenNewQueryTab(sql);
    }

    // ── 匯出（委派給 ViewModel 命令）─────────────────────────

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ExportCsvCommand?.CanExecute(null) == true)
            Vm.ExportCsvCommand.Execute(null);
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ExportXlsxCommand?.CanExecute(null) == true)
            Vm.ExportXlsxCommand.Execute(null);
    }

    // ── Filter Bar ───────────────────────────────────────────
    // 用 FindName 取控件，避免 XAML x:Name 生成問題
    private System.Windows.Controls.Border? GetFilterBar()
        => FindName("FilterBar") as System.Windows.Controls.Border;
    private System.Windows.Controls.TextBox? GetFilterBox()
        => FindName("FilterBox") as System.Windows.Controls.TextBox;
    private System.Windows.Controls.ComboBox? GetFilterColumnCombo()
        => FindName("FilterColumnCombo") as System.Windows.Controls.ComboBox;
    private System.Windows.Controls.ComboBox? GetFilterOpCombo()
        => FindName("FilterOpCombo") as System.Windows.Controls.ComboBox;

    private void ToggleFilterBar()
    {
        var bar = GetFilterBar();
        if (bar == null) return;
        if (bar.Visibility == Visibility.Collapsed)
        {
            bar.Visibility = Visibility.Visible;
            RefreshFilterColumns();
            GetFilterBox()?.Focus();
        }
        else
        {
            FilterClose_Click(this, new RoutedEventArgs());
        }
    }

    private void RefreshFilterColumns()
    {
        if (Vm?.ResultData == null) return;
        var combo = GetFilterColumnCombo();
        if (combo == null) return;
        var cols = new List<string> { "（所有欄位）" };
        cols.AddRange(Vm.ResultData.Columns.Cast<System.Data.DataColumn>()
                       .Select(col => col.ColumnName));
        combo.ItemsSource   = cols;
        combo.SelectedIndex = 0;
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter();

    private void ApplyFilter()
    {
        if (Vm?.ResultData == null) return;
        var filterBox = GetFilterBox();
        var opCombo   = GetFilterOpCombo();
        var colCombo  = GetFilterColumnCombo();
        if (filterBox == null) return;

        var keyword = filterBox.Text.Trim();
        var op      = (opCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "包含";
        var col     = (colCombo?.SelectedIndex ?? 0) <= 0 ? null : colCombo?.SelectedItem?.ToString();

        var view = Vm.ResultData.DefaultView;

        if (string.IsNullOrEmpty(keyword) && op != "為空" && op != "非空")
        {
            view.RowFilter     = "";
            ResultGrid.ItemsSource = view;
            return;
        }

        try
        {
            string filter;
            if (col == null)
            {
                var parts = Vm.ResultData.Columns.Cast<System.Data.DataColumn>()
                    .Select(dc => BuildFilter(dc.ColumnName, keyword, op))
                    .Where(f => f != null);
                filter = string.Join(" OR ", parts);
            }
            else
            {
                filter = BuildFilter(col, keyword, op) ?? "";
            }
            view.RowFilter     = filter;
            ResultGrid.ItemsSource = view;
        }
        catch { /* 忽略無效 filter */ }
    }

    private static string? BuildFilter(string colName, string keyword, string op)
    {
        var c  = colName.Replace("]", "]]");
        var kw = keyword.Replace("'", "''");
        return op switch
        {
            "包含"   => $"Convert([{c}], 'System.String') LIKE '%{kw}%'",
            "等於"   => $"Convert([{c}], 'System.String') = '{kw}'",
            "開頭為" => $"Convert([{c}], 'System.String') LIKE '{kw}%'",
            "結尾為" => $"Convert([{c}], 'System.String') LIKE '%{kw}'",
            "不含"   => $"Convert([{c}], 'System.String') NOT LIKE '%{kw}%'",
            "為空"   => $"[{c}] IS NULL OR Convert([{c}], 'System.String') = ''",
            "非空"   => $"[{c}] IS NOT NULL AND Convert([{c}], 'System.String') <> ''",
            _        => null
        };
    }

    private void FilterClear_Click(object sender, RoutedEventArgs e)
    {
        var box = GetFilterBox();
        if (box != null) box.Clear();
        if (Vm?.ResultData != null) Vm.ResultData.DefaultView.RowFilter = "";
        ResultGrid.ItemsSource = Vm?.ResultData?.DefaultView;
    }

    private void FilterClose_Click(object sender, RoutedEventArgs e)
    {
        FilterClear_Click(sender, e);
        var bar = GetFilterBar();
        if (bar != null) bar.Visibility = Visibility.Collapsed;
    }

    // ── 結果快照 / 比較 ──────────────────────────────────────
    private void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ResultData == null || Vm.ResultData.Rows.Count == 0)
        { MessageBox.Show("目前沒有查詢結果可保留", "提示"); return; }
        var label = $"快照 {Vm.Snapshots.Count + 1}  [{DateTime.Now:HH:mm:ss}]  {Vm.ResultData.Rows.Count} 筆";
        Vm.SaveSnapshot(label);
        MessageBox.Show($"已保留：{label}", "完成", MessageBoxButton.OK, MessageBoxImage.None);
    }

    private void CompareSnapshots_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.Snapshots.Count == 0)
        { MessageBox.Show("尚無保留的結果快照，請先點擊「📌 保留結果」", "提示"); return; }
        // 若有目前結果，也加入供選擇
        var allSnapshots = Vm.Snapshots.ToList();
        if (Vm.ResultData != null && Vm.ResultData.Rows.Count > 0)
        {
            allSnapshots.Insert(0, new ResultSnapshot
            {
                Label = $"[目前結果]  {Vm.ResultData.Rows.Count} 筆",
                Data  = Vm.ResultData.Copy()
            });
        }
        var win = new ResultCompareWindow(allSnapshots)
        {
            Owner = Window.GetWindow(this)
        };
        win.Show();
    }

    // ── Find / Replace ──────────────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FindReplace.Open(replaceMode: false);
            e.Handled = true;
        }
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FindReplace.Open(replaceMode: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindReplace.Visibility == System.Windows.Visibility.Visible)
        {
            FindReplace.Close();
            e.Handled = true;
        }
    }

    private void ToggleFilter_Click(object sender, RoutedEventArgs e)
        => ToggleFilterBar();

    // ── JSON 欄位雙擊檢視 ────────────────────────────────────
    private void ResultGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultGrid.CurrentCell.Item is not System.Data.DataRowView drv) return;
        if (ResultGrid.CurrentCell.Column is not System.Windows.Controls.DataGridBoundColumn col) return;

        var binding = col.Binding as System.Windows.Data.Binding;
        var colName = binding?.Path?.Path;
        if (colName == null) return;

        var raw = drv.Row[colName];
        if (raw == null || raw == DBNull.Value) return;

        // ── BLOB / byte[] preview ────────────────────────────
        if (raw is byte[] bytes)
        {
            var preview = new BlobPreviewDialog(bytes, colName) { Owner = Window.GetWindow(this) };
            preview.Show();
            e.Handled = true;
            return;
        }

        var value = raw.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(value)) return;

        // ── JSON viewer ───────────────────────────────────────
        var trimmed = value.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            var win = new JsonViewerDialog(value) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && win.ResultJson != value)
            {
                try { drv.Row[colName] = win.ResultJson; }
                catch { /* read-only column */ }
            }
            e.Handled = true;
        }
    }

    // ── FK 跳轉 ──────────────────────────────────────────────
    private async void FkJump_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (ResultGrid.CurrentCell.Item is not System.Data.DataRowView drv) return;
        if (ResultGrid.CurrentCell.Column is not System.Windows.Controls.DataGridBoundColumn col) return;

        var binding = col.Binding as System.Windows.Data.Binding;
        var colName = binding?.Path?.Path;
        if (colName == null) return;

        var cellValue = drv.Row[colName]?.ToString() ?? "";
        if (string.IsNullOrEmpty(cellValue)) return;

        // Load FK info for current DB
        var db = Vm.SelectedDatabase;
        if (string.IsNullOrEmpty(db)) return;

        try
        {
            var fks = await App.ConnectionService.GetForeignKeysAsync(db);
            // Find FK where column name matches
            var match = fks.FirstOrDefault(fk =>
                fk.Column.Equals(colName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                System.Windows.MessageBox.Show(
                    $"欄位 [{colName}] 沒有外鍵定義。",
                    "找不到外鍵",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // Build jump query
            var sql = $"SELECT * FROM `{match.ReferencedTable}` WHERE `{match.ReferencedColumn}` = {QuoteValue(cellValue)} LIMIT 500;";

            // Open new tab with query
            var mainVm = System.Windows.Application.Current.MainWindow?.DataContext as MySQLManager.ViewModels.MainViewModel;
            if (mainVm != null)
            {
                mainVm.OpenNewQueryTab(sql, db);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"無法取得外鍵資訊：{ex.Message}", "錯誤",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private static string QuoteValue(string v)
    {
        if (long.TryParse(v, out _) || double.TryParse(v, out _))
            return v;
        return $"'{v.Replace("'", "\'")}'";
    }

    // ── BLOB 欄位自動偵測 ────────────────────────────────────
    private void ResultGrid_AutoGeneratingColumn(object sender,
        System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyType == typeof(byte[]))
        {
            // Replace default column with a template column showing [BLOB xKB]
            var tplCol = new System.Windows.Controls.DataGridTemplateColumn
            {
                Header      = e.Column.Header,
                SortMemberPath = e.PropertyName,
            };
            var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding(e.PropertyName)
                {
                    Converter = new BlobSizeConverter()
                });
            factory.SetValue(System.Windows.Controls.TextBlock.ForegroundProperty,
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x7B, 0x61, 0xFF)));
            factory.SetValue(System.Windows.Controls.TextBlock.FontStyleProperty,
                System.Windows.FontStyles.Italic);
            tplCol.CellTemplate = new System.Windows.DataTemplate { VisualTree = factory };
            e.Column = tplCol;
        }
    }

    // ── Excel-style column header filter ─────────────────────────────────
    private readonly Dictionary<string, HashSet<string>> _colFilters = new();
    private System.Windows.Controls.Primitives.Popup? _filterPopup;

    private void ColumnFilterBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (Vm?.ResultData == null) return;

        // Get column name from header template binding
        var header = btn.Tag as System.Windows.Controls.Primitives.DataGridColumnHeader;
        var colName = header?.Content?.ToString();
        if (colName == null) return;

        // Collect distinct values for this column
        var dt = Vm.ResultData;
        if (!dt.Columns.Contains(colName)) return;

        var distinct = new List<string>();
        var seen = new HashSet<string>();
        foreach (System.Data.DataRow r in dt.Rows)
        {
            var v = r[colName]?.ToString() ?? "";
            if (seen.Add(v)) distinct.Add(v);
        }
        distinct.Sort();

        // Build popup
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            StaysOpen        = false,
            AllowsTransparency = true,
            PopupAnimation   = System.Windows.Controls.Primitives.PopupAnimation.Fade,
            PlacementTarget  = btn,
            Placement        = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        var filterCtrl = new ColumnFilterPopup();
        _colFilters.TryGetValue(colName, out var currentFilter);
        filterCtrl.Populate(distinct, currentFilter);
        filterCtrl.FilterApplied += (filter) =>
        {
            if (filter == null)
                _colFilters.Remove(colName);
            else
                _colFilters[colName] = filter;

            ApplyColumnFilters();
            popup.IsOpen = false;
        };

        popup.Child   = filterCtrl;
        popup.IsOpen  = true;
        _filterPopup  = popup;

        // Highlight header if filtered
        btn.Foreground = _colFilters.ContainsKey(colName)
            ? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE6, 0x51, 0x00))
            : System.Windows.SystemColors.ControlTextBrush;

        e.Handled = true;
    }

    private void ApplyColumnFilters()
    {
        if (Vm?.ResultData == null) return;

        if (_colFilters.Count == 0)
        {
            ResultGrid.ItemsSource = Vm.ResultData.DefaultView;
            return;
        }

        // Build row filter expression
        var parts = new List<string>();
        foreach (var (col, values) in _colFilters)
        {
            if (!Vm.ResultData.Columns.Contains(col)) continue;
            var quotedVals = values.Select(v =>
                $"Convert([{col}], 'System.String') = '{v.Replace("'", "''")}'");
            parts.Add("(" + string.Join(" OR ", quotedVals) + ")");
        }

        var view = Vm.ResultData.DefaultView;
        view.RowFilter = string.Join(" AND ", parts);
        ResultGrid.ItemsSource = view;
    }

    // Clear column filters when new query runs
    public void ClearColumnFilters()
    {
        _colFilters.Clear();
        if (Vm?.ResultData != null)
            ResultGrid.ItemsSource = Vm.ResultData.DefaultView;
    }

    private void AiIndex_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var sql = Vm?.SqlText?.Trim();
        if (string.IsNullOrEmpty(sql)) { System.Windows.MessageBox.Show("請先輸入 SQL"); return; }
        var db = Vm?.SelectedDatabase ?? "";
        new IndexSuggestWindow(sql, db) { Owner = Window.GetWindow(this) }.Show();
    }

    // ── Column width memory ───────────────────────────────────────────────
    private string? _currentTableKey;

    private void ApplyColumnWidths(string tableKey)
    {
        _currentTableKey = tableKey;
        var widths = App.ColumnWidthService.GetWidths(tableKey);
        if (widths == null) return;

        // Wait for columns to be generated then apply
        ResultGrid.Dispatcher.InvokeAsync(() =>
        {
            foreach (var col in ResultGrid.Columns)
            {
                var header = col.Header?.ToString();
                if (header != null && widths.TryGetValue(header, out var w) && w > 10)
                    col.Width = new System.Windows.Controls.DataGridLength(w);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SaveCurrentColumnWidths()
    {
        if (_currentTableKey == null) return;
        var widths = ResultGrid.Columns
            .Where(col => col.ActualWidth > 0 && col.Header != null)
            .Select(col => (Col: col.Header.ToString()!, Width: col.ActualWidth));
        App.ColumnWidthService.SaveAll(_currentTableKey, widths);
    }

    // ── 開啟 / 儲存 SQL 檔案 ─────────────────────────────────────────────
    private void OpenSqlFile_Click(object sender, System.Windows.RoutedEventArgs e)
        => OpenSqlFile();

    private void OpenSqlFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "開啟 SQL 檔案",
            Filter = "SQL 檔案 (*.sql)|*.sql|所有檔案 (*.*)|*.*",
            DefaultExt = ".sql"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = System.IO.File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            SqlEditor.Text = text;
            SqlEditor.ScrollToHome();
            // Show filename in window title area
            if (Window.GetWindow(this) is Window win)
                win.Title = $"MySQL Manager — {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"無法開啟檔案：{ex.Message}", "錯誤",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void SaveSqlFile_Click(object sender, System.Windows.RoutedEventArgs e)
        => SaveSqlFile();

    private void SaveSqlFile()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "另存 SQL 檔案",
            Filter     = "SQL 檔案 (*.sql)|*.sql|所有檔案 (*.*)|*.*",
            DefaultExt = ".sql",
            FileName   = "query.sql"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            System.IO.File.WriteAllText(dlg.FileName, SqlEditor.Text,
                System.Text.Encoding.UTF8);
            System.Windows.MessageBox.Show(
                $"已儲存：{System.IO.Path.GetFileName(dlg.FileName)}", "完成",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"無法儲存：{ex.Message}", "錯誤",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

}
