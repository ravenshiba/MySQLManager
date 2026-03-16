using System.Collections.ObjectModel;

namespace MySQLManager.Models;

public enum DbNodeType
{
    Server,
    Database,
    TablesFolder,
    Table,
    ViewsFolder,
    View,
    ProceduresFolder,
    Procedure,
    Column
}

/// <summary>
/// 左側資料庫樹狀結構節點
/// </summary>
public class DbTreeNode
{
    public string Name { get; set; } = string.Empty;
    public DbNodeType NodeType { get; set; }
    public string? ParentDatabase { get; set; }
    public string? ParentTable { get; set; }
    public string Icon => NodeType switch
    {
        DbNodeType.Server       => "🖥️",
        DbNodeType.Database     => "🗄️",
        DbNodeType.TablesFolder => "📁",
        DbNodeType.Table        => "📋",
        DbNodeType.ViewsFolder  => "📁",
        DbNodeType.View         => "👁️",
        DbNodeType.Column       => "📌",
        _                       => "📄"
    };
    public bool IsExpanded { get; set; } = false;
    public bool IsLoading { get; set; } = false;
    public ObservableCollection<DbTreeNode> Children { get; set; } = new();
}

/// <summary>
/// 資料表欄位資訊
/// </summary>
public class ColumnInfo
{
    public string Field { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Null { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Default { get; set; }
    public string Extra { get; set; } = string.Empty;
}

/// <summary>
/// SQL 查詢結果
/// </summary>
public class QueryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public System.Data.DataTable? Data { get; set; }
    public long RowsAffected { get; set; }
    public double ExecutionTimeMs { get; set; }
    public string? Sql { get; set; }
}

/// <summary>
/// 搜尋結果項目（資料庫/資料表/欄位）
/// </summary>
public class SearchResultItem
{
    public string Name  { get; set; } = string.Empty;
    public string Path  { get; set; } = string.Empty;   // e.g. "mydb / users"
    public DbNodeType NodeType { get; set; }
    public string? Database   { get; set; }
    public string? Table      { get; set; }

    public string Icon => NodeType switch
    {
        DbNodeType.Database => "🗄️",
        DbNodeType.Table    => "📋",
        DbNodeType.View     => "👁️",
        DbNodeType.Column   => "📌",
        _                   => "📄"
    };
}
