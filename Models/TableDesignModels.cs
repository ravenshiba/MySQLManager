using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MySQLManager.Models;

// ── 欄位定義 ──────────────────────────────────────────────────

public class ColumnDefinition : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _dataType = "VARCHAR";
    private int? _length = 255;
    private int? _decimals;
    private bool _isNullable = true;
    private bool _isPrimaryKey;
    private bool _isAutoIncrement;
    private bool _isUnsigned;
    private string? _defaultValue;
    private string? _comment;
    private bool _isNew = true;      // 新增的欄位
    private bool _isModified;        // 已修改的現有欄位
    private string _originalName = string.Empty;

    public string Name              { get => _name;            set { _name = value; OnPropChanged(); MarkModified(); } }
    public string DataType          { get => _dataType;        set { _dataType = value; OnPropChanged(); MarkModified(); UpdateLengthVisibility(); } }
    public int? Length              { get => _length;          set { _length = value; OnPropChanged(); MarkModified(); } }
    public int? Decimals            { get => _decimals;        set { _decimals = value; OnPropChanged(); MarkModified(); } }
    public bool IsNullable          { get => _isNullable;      set { _isNullable = value; OnPropChanged(); MarkModified(); } }
    public bool IsPrimaryKey        { get => _isPrimaryKey;    set { _isPrimaryKey = value; OnPropChanged(); MarkModified(); if(value) { IsNullable = false; } } }
    public bool IsAutoIncrement     { get => _isAutoIncrement; set { _isAutoIncrement = value; OnPropChanged(); MarkModified(); } }
    public bool IsUnsigned          { get => _isUnsigned;      set { _isUnsigned = value; OnPropChanged(); MarkModified(); } }
    public string? DefaultValue     { get => _defaultValue;    set { _defaultValue = value; OnPropChanged(); MarkModified(); } }
    public string? Comment          { get => _comment;         set { _comment = value; OnPropChanged(); MarkModified(); } }
    public bool IsNew               { get => _isNew;           set { _isNew = value; OnPropChanged(); } }
    public bool IsModified          { get => _isModified;      set { _isModified = value; OnPropChanged(); } }
    public string OriginalName      { get => _originalName;    set => _originalName = value; }

    // 是否顯示長度欄
    private bool _showLength = true;
    public bool ShowLength { get => _showLength; set { _showLength = value; OnPropChanged(); } }

    private void MarkModified() { if (!_isNew) _isModified = true; }

    private void UpdateLengthVisibility()
    {
        ShowLength = _dataType is "VARCHAR" or "CHAR" or "DECIMAL" or "FLOAT"
                                or "DOUBLE" or "BINARY" or "VARBINARY";
    }

    /// <summary>產生此欄位的 DDL 片段</summary>
    public string ToColumnDdl()
    {
        var parts = new System.Text.StringBuilder();
        parts.Append($"`{Name}` {DataType}");

        if (ShowLength && Length.HasValue)
        {
            if (Decimals.HasValue && DataType is "DECIMAL" or "FLOAT" or "DOUBLE")
                parts.Append($"({Length},{Decimals})");
            else
                parts.Append($"({Length})");
        }

        if (IsUnsigned) parts.Append(" UNSIGNED");
        parts.Append(IsNullable ? " NULL" : " NOT NULL");
        if (IsAutoIncrement) parts.Append(" AUTO_INCREMENT");

        if (DefaultValue != null && !IsAutoIncrement)
        {
            var numTypes = new[] { "INT", "TINYINT", "SMALLINT", "MEDIUMINT", "BIGINT",
                                   "FLOAT", "DOUBLE", "DECIMAL", "BIT" };
            bool isNumeric = System.Array.Exists(numTypes, t => DataType.StartsWith(t));
            parts.Append(isNumeric
                ? $" DEFAULT {DefaultValue}"
                : $" DEFAULT '{DefaultValue}'");
        }

        if (!string.IsNullOrWhiteSpace(Comment))
            parts.Append($" COMMENT '{Comment.Replace("'", "''")}'");

        return parts.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── 索引定義 ──────────────────────────────────────────────────

public enum IndexType { PRIMARY, UNIQUE, INDEX, FULLTEXT }

public class IndexDefinition : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private IndexType _indexType = IndexType.INDEX;
    private string _columns = string.Empty;
    private bool _isNew = true;

    public string Name              { get => _name;      set { _name = value; OnPropChanged(); } }
    public IndexType IndexType      { get => _indexType; set { _indexType = value; OnPropChanged(); } }
    public string Columns           { get => _columns;   set { _columns = value; OnPropChanged(); } }
    public bool IsNew               { get => _isNew;     set { _isNew = value; OnPropChanged(); } }

    public string ToIndexDdl()
    {
        var cols = string.Join(", ", Columns.Split(',', System.StringSplitOptions.TrimEntries)
                                            .Select(c => $"`{c}`"));
        return IndexType switch
        {
            IndexType.PRIMARY  => $"PRIMARY KEY ({cols})",
            IndexType.UNIQUE   => $"UNIQUE KEY `{Name}` ({cols})",
            IndexType.FULLTEXT => $"FULLTEXT KEY `{Name}` ({cols})",
            _                  => $"KEY `{Name}` ({cols})"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

// ── 資料表設計資訊 ────────────────────────────────────────────

public class TableDesign
{
    public string Database { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Engine { get; set; } = "InnoDB";
    public string Charset { get; set; } = "utf8mb4";
    public string Collation { get; set; } = "utf8mb4_unicode_ci";
    public string? Comment { get; set; }
    public bool IsNewTable { get; set; } = true;
    public ObservableCollection<ColumnDefinition> Columns { get; set; } = new();
    public ObservableCollection<IndexDefinition> Indexes { get; set; } = new();
}
