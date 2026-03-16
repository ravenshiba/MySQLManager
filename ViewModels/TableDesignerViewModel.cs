using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Models;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

public class TableDesignerViewModel : BaseViewModel
{
    private readonly ConnectionService _connService;

    // ── 基本資訊 ──────────────────────────────────────────────

    public TableDesign Design { get; }

    private string _tableName = string.Empty;
    public string TableName
    {
        get => _tableName;
        set { SetProperty(ref _tableName, value); Design.TableName = value; }
    }

    private string _engine = "InnoDB";
    public string Engine { get => _engine; set { SetProperty(ref _engine, value); Design.Engine = value; } }

    private string _charset = "utf8mb4";
    public string Charset { get => _charset; set { SetProperty(ref _charset, value); Design.Charset = value; } }

    private string _collation = "utf8mb4_unicode_ci";
    public string Collation { get => _collation; set { SetProperty(ref _collation, value); Design.Collation = value; } }

    private string? _tableComment;
    public string? TableComment { get => _tableComment; set { SetProperty(ref _tableComment, value); Design.Comment = value; } }

    public string Database { get; set; }

    // ── 欄位 ──────────────────────────────────────────────────

    public ObservableCollection<ColumnDefinition> Columns => Design.Columns;

    private ColumnDefinition? _selectedColumn;
    public ColumnDefinition? SelectedColumn
    {
        get => _selectedColumn;
        set => SetProperty(ref _selectedColumn, value);
    }

    // ── 索引 ──────────────────────────────────────────────────

    public ObservableCollection<IndexDefinition> Indexes => Design.Indexes;

    private IndexDefinition? _selectedIndex;
    public IndexDefinition? SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    // ── 狀態 ──────────────────────────────────────────────────

    private string _previewSql = string.Empty;
    public string PreviewSql { get => _previewSql; set => SetProperty(ref _previewSql, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

    private int _activeTab;
    public int ActiveTab { get => _activeTab; set => SetProperty(ref _activeTab, value); }

    // ── 常數清單 ──────────────────────────────────────────────

    public string[] DataTypes { get; } =
    {
        "INT", "TINYINT", "SMALLINT", "MEDIUMINT", "BIGINT",
        "FLOAT", "DOUBLE", "DECIMAL",
        "VARCHAR", "CHAR", "TEXT", "TINYTEXT", "MEDIUMTEXT", "LONGTEXT",
        "DATE", "DATETIME", "TIMESTAMP", "TIME", "YEAR",
        "BLOB", "TINYBLOB", "MEDIUMBLOB", "LONGBLOB",
        "ENUM", "SET", "JSON", "BIT", "BOOLEAN",
        "BINARY", "VARBINARY"
    };

    public string[] Engines { get; } = { "InnoDB", "MyISAM", "MEMORY", "CSV", "ARCHIVE" };

    public string[] Charsets { get; } =
        { "utf8mb4", "utf8", "latin1", "ascii", "gbk", "big5" };

    public string[] Collations { get; } =
    {
        "utf8mb4_unicode_ci", "utf8mb4_general_ci", "utf8mb4_bin",
        "utf8_unicode_ci", "utf8_general_ci",
        "latin1_swedish_ci", "ascii_general_ci"
    };

    public IndexType[] IndexTypes { get; } =
        { IndexType.PRIMARY, IndexType.UNIQUE, IndexType.INDEX, IndexType.FULLTEXT };

    // ── 命令 ──────────────────────────────────────────────────

    public RelayCommand AddColumnCommand    { get; }
    public RelayCommand DeleteColumnCommand { get; }
    public RelayCommand MoveUpCommand       { get; }
    public RelayCommand MoveDownCommand     { get; }
    public RelayCommand AddIndexCommand     { get; }
    public RelayCommand DeleteIndexCommand  { get; }
    public RelayCommand RefreshPreviewCommand { get; }
    public AsyncRelayCommand SaveCommand    { get; }

    #pragma warning disable CS0067
    public event Action? CloseRequested;
    #pragma warning restore CS0067

    // ── 建構子 ────────────────────────────────────────────────

    public TableDesignerViewModel(string database, TableDesign? existingDesign = null)
    {
        _connService = (System.Windows.Application.Current.MainWindow?.DataContext as MySQLManager.ViewModels.MainViewModel)?.ActiveSession?.ConnectionService ?? App.ConnectionService;
        Database = database;
        Design = existingDesign ?? new TableDesign { Database = database, IsNewTable = true };

        TableName    = Design.TableName;
        Engine       = Design.Engine;
        Charset      = Design.Charset;
        Collation    = Design.Collation;
        TableComment = Design.Comment;

        AddColumnCommand      = new RelayCommand(_ => AddColumn());
        DeleteColumnCommand   = new RelayCommand(_ => DeleteColumn(), _ => SelectedColumn != null);
        MoveUpCommand         = new RelayCommand(_ => MoveColumn(-1), _ => SelectedColumn != null);
        MoveDownCommand       = new RelayCommand(_ => MoveColumn(1),  _ => SelectedColumn != null);
        AddIndexCommand       = new RelayCommand(_ => AddIndex());
        DeleteIndexCommand    = new RelayCommand(_ => DeleteIndex(),  _ => SelectedIndex != null);
        RefreshPreviewCommand = new RelayCommand(_ => RefreshPreview());
        SaveCommand           = new AsyncRelayCommand(SaveAsync, () => !IsBusy);

        // 預設加一個 id 欄位
        if (Design.IsNewTable && Design.Columns.Count == 0)
        {
            AddColumn(new ColumnDefinition
            {
                Name = "id", DataType = "INT", Length = null,
                IsNullable = false, IsPrimaryKey = true, IsAutoIncrement = true,
                IsUnsigned = true, Comment = "Primary key"
            });
        }

        RefreshPreview();
    }

    // ── 欄位操作 ──────────────────────────────────────────────

    private void AddColumn(ColumnDefinition? col = null)
    {
        var newCol = col ?? new ColumnDefinition
        {
            Name = $"column{Columns.Count + 1}",
            DataType = "VARCHAR", Length = 255, IsNullable = true
        };
        Columns.Add(newCol);
        SelectedColumn = newCol;
        RefreshPreview();
    }

    private void DeleteColumn()
    {
        if (SelectedColumn == null) return;
        var idx = Columns.IndexOf(SelectedColumn);
        Columns.Remove(SelectedColumn);
        SelectedColumn = Columns.Count > 0
            ? Columns[Math.Min(idx, Columns.Count - 1)]
            : null;
        RefreshPreview();
    }

    private void MoveColumn(int delta)
    {
        if (SelectedColumn == null) return;
        var idx = Columns.IndexOf(SelectedColumn);
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= Columns.Count) return;
        Columns.Move(idx, newIdx);
        RefreshPreview();
    }

    // ── 索引操作 ──────────────────────────────────────────────

    private void AddIndex()
    {
        var idx = new IndexDefinition
        {
            Name = $"idx_{TableName}_{Indexes.Count + 1}",
            IndexType = IndexType.INDEX,
            Columns = Columns.FirstOrDefault()?.Name ?? ""
        };
        Indexes.Add(idx);
        SelectedIndex = idx;
        RefreshPreview();
    }

    private void DeleteIndex()
    {
        if (SelectedIndex == null) return;
        Indexes.Remove(SelectedIndex);
        SelectedIndex = null;
        RefreshPreview();
    }

    // ── SQL 預覽 ──────────────────────────────────────────────

    public void RefreshPreview()
    {
        PreviewSql = Design.IsNewTable
            ? GenerateCreateSql()
            : GenerateAlterSql();
    }

    private string GenerateCreateSql()
    {
        if (string.IsNullOrWhiteSpace(TableName))
            return "-- 請輸入資料表名稱";
        if (Columns.Count == 0)
            return "-- 請至少新增一個欄位";

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE `{Database}`.`{TableName}` (");

        var defs = new System.Collections.Generic.List<string>();
        foreach (var col in Columns)
            defs.Add("  " + col.ToColumnDdl());

        // PRIMARY KEY
        var pkCols = Columns.Where(c => c.IsPrimaryKey).Select(c => $"`{c.Name}`").ToList();
        if (pkCols.Count > 0 && !Columns.Any(c => c.IsPrimaryKey && Columns.Count(x => x.IsPrimaryKey) == 1
                                                                   && c.IsAutoIncrement))
            defs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        // 其他索引
        foreach (var idx in Indexes.Where(i => i.IndexType != IndexType.PRIMARY))
            defs.Add("  " + idx.ToIndexDdl());

        sb.AppendLine(string.Join(",\n", defs));
        sb.Append($") ENGINE={Engine} DEFAULT CHARSET={Charset} COLLATE={Collation}");

        if (!string.IsNullOrWhiteSpace(TableComment))
            sb.Append($" COMMENT='{TableComment}'");

        sb.Append(";");
        return sb.ToString();
    }

    private string GenerateAlterSql()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- ALTER TABLE `{Database}`.`{TableName}`");

        foreach (var col in Columns.Where(c => c.IsNew))
            sb.AppendLine($"ALTER TABLE `{Database}`.`{TableName}` ADD COLUMN {col.ToColumnDdl()};");

        foreach (var col in Columns.Where(c => c.IsModified && !c.IsNew))
        {
            if (col.OriginalName != col.Name)
                sb.AppendLine($"ALTER TABLE `{Database}`.`{TableName}` CHANGE COLUMN `{col.OriginalName}` {col.ToColumnDdl()};");
            else
                sb.AppendLine($"ALTER TABLE `{Database}`.`{TableName}` MODIFY COLUMN {col.ToColumnDdl()};");
        }

        foreach (var idx in Indexes.Where(i => i.IsNew))
            sb.AppendLine($"ALTER TABLE `{Database}`.`{TableName}` ADD {idx.ToIndexDdl()};");

        return sb.ToString().Trim();
    }

    // ── 儲存 ─────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(TableName))
        {
            HasError = true;
            StatusMessage = "❌ 請輸入資料表名稱";
            return;
        }
        if (Columns.Count == 0)
        {
            HasError = true;
            StatusMessage = "❌ 請至少新增一個欄位";
            return;
        }

        IsBusy = true;
        HasError = false;
        StatusMessage = "執行中...";
        RefreshPreview();

        try
        {
            if (Design.IsNewTable)
            {
                var sql = GenerateCreateSql();
                var result = await _connService.ExecuteNonQueryAsync(sql);
                if (result.Success)
                {
                    StatusMessage = $"✅ 資料表 `{TableName}` 建立成功！";
                    foreach (var col in Columns) col.IsNew = false;
                    Design.IsNewTable = false;
                }
                else
                {
                    HasError = true;
                    StatusMessage = $"❌ {result.ErrorMessage}";
                }
            }
            else
            {
                var sqls = GenerateAlterSql().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Where(s => !s.StartsWith("--")).ToArray();
                foreach (var sql in sqls)
                {
                    var result = await _connService.ExecuteNonQueryAsync(sql.Trim());
                    if (!result.Success)
                    {
                        HasError = true;
                        StatusMessage = $"❌ {result.ErrorMessage}";
                        return;
                    }
                }
                StatusMessage = $"✅ 資料表 `{TableName}` 更新成功！";
                foreach (var col in Columns) { col.IsModified = false; col.IsNew = false; col.OriginalName = col.Name; }
                foreach (var idx in Indexes) idx.IsNew = false;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"❌ 例外：{ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
