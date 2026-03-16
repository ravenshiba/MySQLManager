using System.Collections.Generic;
using System.Linq;
using MySQLManager.Models;

namespace MySQLManager.Services;

public enum DiffType { Same, Added, Removed, Modified }

public class SchemaDiffItem
{
    public DiffType  Kind        { get; set; }
    public string    ObjectType  { get; set; } = "";  // "Column" / "Index"
    public string    Name        { get; set; } = "";
    public string?   LeftValue   { get; set; }
    public string?   RightValue  { get; set; }
    public string    KindLabel   => Kind switch
    {
        DiffType.Added    => "＋ 新增",
        DiffType.Removed  => "－ 移除",
        DiffType.Modified => "△ 修改",
        _                 => "＝ 相同"
    };
    public string KindColor => Kind switch
    {
        DiffType.Added    => "#66BB6A",
        DiffType.Removed  => "#EF5350",
        DiffType.Modified => "#FFA726",
        _                 => "#546E7A"
    };
}

public class TableComparePair
{
    public string   TableName   { get; set; } = "";
    public DiffType Kind        { get; set; }
    public List<SchemaDiffItem> ColumnDiffs { get; set; } = new();
    public List<SchemaDiffItem> IndexDiffs  { get; set; } = new();
    public int      DiffCount   => ColumnDiffs.Count(d => d.Kind != DiffType.Same)
                                 + IndexDiffs.Count(d => d.Kind != DiffType.Same);
    public string   KindLabel   => Kind switch
    {
        DiffType.Added   => "＋ 僅右側有",
        DiffType.Removed => "－ 僅左側有",
        _                => DiffCount > 0 ? $"△ {DiffCount} 項差異" : "＝ 相同"
    };
    public string KindColor => Kind switch
    {
        DiffType.Added   => "#66BB6A",
        DiffType.Removed => "#EF5350",
        _                => DiffCount > 0 ? "#FFA726" : "#546E7A"
    };
}

public class SchemaCompareService
{
    private readonly ConnectionService _conn;
    public SchemaCompareService(ConnectionService conn) => _conn = conn;

    public async System.Threading.Tasks.Task<List<TableComparePair>> CompareAsync(
        string leftDb, string rightDb,
        System.IProgress<string>? progress = null)
    {
        progress?.Report("載入資料表清單…");
        var leftTables  = (await _conn.GetTablesAsync(leftDb)).ToHashSet();
        var rightTables = (await _conn.GetTablesAsync(rightDb)).ToHashSet();

        var allTables = leftTables.Union(rightTables).OrderBy(t => t).ToList();
        var result    = new List<TableComparePair>();

        foreach (var table in allTables)
        {
            progress?.Report($"比較 {table}…");
            bool inLeft  = leftTables.Contains(table);
            bool inRight = rightTables.Contains(table);

            if (!inLeft)
            {
                result.Add(new TableComparePair { TableName = table, Kind = DiffType.Added });
                continue;
            }
            if (!inRight)
            {
                result.Add(new TableComparePair { TableName = table, Kind = DiffType.Removed });
                continue;
            }

            // 兩邊都有 → 比較欄位與索引
            var leftSchema  = await _conn.GetTableSchemaAsync(leftDb,  table);
            var rightSchema = await _conn.GetTableSchemaAsync(rightDb, table);

            var pair = new TableComparePair { TableName = table, Kind = DiffType.Same };
            pair.ColumnDiffs = CompareColumns(leftSchema.Columns, rightSchema.Columns);
            pair.IndexDiffs  = CompareIndexes(leftSchema.Indexes,  rightSchema.Indexes);
            result.Add(pair);
        }

        return result;
    }

    private static List<SchemaDiffItem> CompareColumns(
        List<ColumnInfo> left, List<ColumnInfo> right)
    {
        var diffs   = new List<SchemaDiffItem>();
        var leftMap = left.ToDictionary(c => c.Field);
        var rightMap = right.ToDictionary(c => c.Field);

        foreach (var col in left)
        {
            if (!rightMap.TryGetValue(col.Field, out var rCol))
            {
                diffs.Add(new SchemaDiffItem
                {
                    Kind = DiffType.Removed, ObjectType = "Column",
                    Name = col.Field, LeftValue = FormatColumn(col)
                });
            }
            else
            {
                var lVal = FormatColumn(col);
                var rVal = FormatColumn(rCol);
                diffs.Add(new SchemaDiffItem
                {
                    Kind = lVal == rVal ? DiffType.Same : DiffType.Modified,
                    ObjectType = "Column", Name = col.Field,
                    LeftValue = lVal, RightValue = rVal
                });
            }
        }
        foreach (var col in right.Where(c => !leftMap.ContainsKey(c.Field)))
            diffs.Add(new SchemaDiffItem
            {
                Kind = DiffType.Added, ObjectType = "Column",
                Name = col.Field, RightValue = FormatColumn(col)
            });
        return diffs;
    }

    private static List<SchemaDiffItem> CompareIndexes(
        List<IndexSchema> left, List<IndexSchema> right)
    {
        var diffs    = new List<SchemaDiffItem>();
        var leftMap  = left.ToDictionary(i => i.KeyName);
        var rightMap = right.ToDictionary(i => i.KeyName);

        foreach (var idx in left)
        {
            if (!rightMap.TryGetValue(idx.KeyName, out var rIdx))
                diffs.Add(new SchemaDiffItem
                {
                    Kind = DiffType.Removed, ObjectType = "Index",
                    Name = idx.KeyName, LeftValue = FormatIndex(idx)
                });
            else
            {
                var lVal = FormatIndex(idx); var rVal = FormatIndex(rIdx);
                diffs.Add(new SchemaDiffItem
                {
                    Kind = lVal == rVal ? DiffType.Same : DiffType.Modified,
                    ObjectType = "Index", Name = idx.KeyName,
                    LeftValue = lVal, RightValue = rVal
                });
            }
        }
        foreach (var idx in right.Where(i => !leftMap.ContainsKey(i.KeyName)))
            diffs.Add(new SchemaDiffItem
            {
                Kind = DiffType.Added, ObjectType = "Index",
                Name = idx.KeyName, RightValue = FormatIndex(idx)
            });
        return diffs;
    }

    private static string FormatColumn(ColumnInfo c) =>
        $"{c.Type} | NULL:{c.Null} | KEY:{c.Key} | DEFAULT:{c.Default ?? "NULL"} | {c.Extra}";

    private static string FormatIndex(IndexSchema i) =>
        $"{i.IndexType} | Col:{i.ColumnName} | Unique:{!i.NonUnique}";
}
