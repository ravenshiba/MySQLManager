using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MySQLManager.Services;

public enum DiffRowKind { Unchanged, Added, Deleted, Modified }

public class DiffRow
{
    public DiffRowKind        Kind          { get; set; }
    public int                RowIndex      { get; set; }   // 來源行號（-1 = 無）
    public Dictionary<string, object?> LeftValues  { get; set; } = new();
    public Dictionary<string, object?> RightValues { get; set; } = new();
    public HashSet<string>    ChangedCols   { get; set; } = new();

    public string KindLabel => Kind switch
    {
        DiffRowKind.Added    => "新增",
        DiffRowKind.Deleted  => "刪除",
        DiffRowKind.Modified => "修改",
        _                    => "",
    };

    public string KindColor => Kind switch
    {
        DiffRowKind.Added    => "#E8F5E9",
        DiffRowKind.Deleted  => "#FEECEC",
        DiffRowKind.Modified => "#FFF8E1",
        _                    => "Transparent",
    };

    public string KindBadgeColor => Kind switch
    {
        DiffRowKind.Added    => "#2E7D32",
        DiffRowKind.Deleted  => "#C62828",
        DiffRowKind.Modified => "#E65100",
        _                    => "Transparent",
    };
}

public class DataDiffResult
{
    public List<DiffRow>  Rows         { get; set; } = new();
    public List<string>   Columns      { get; set; } = new();
    public string?        KeyColumn    { get; set; }
    public int            AddedCount   => Rows.Count(r => r.Kind == DiffRowKind.Added);
    public int            DeletedCount => Rows.Count(r => r.Kind == DiffRowKind.Deleted);
    public int            ModifiedCount=> Rows.Count(r => r.Kind == DiffRowKind.Modified);
    public int            UnchangedCount=> Rows.Count(r => r.Kind == DiffRowKind.Unchanged);
    public int            TotalDiff    => AddedCount + DeletedCount + ModifiedCount;
}

public class DataDiffService
{
    private readonly ConnectionService _conn;

    public DataDiffService(ConnectionService conn) => _conn = conn;

    /// <summary>
    /// 比對兩個資料表（可跨資料庫），回傳逐行差異
    /// </summary>
    public async Task<DataDiffResult> CompareAsync(
        string leftDb,  string leftTable,
        string rightDb, string rightTable,
        string? keyColumn      = null,
        string? whereClause    = null,
        string? orderByClause  = null,
        IProgress<string>? progress = null)
    {
        progress?.Report("載入左表資料...");
        var leftSql  = BuildSql(leftDb,  leftTable,  whereClause, orderByClause);
        var rightSql = BuildSql(rightDb, rightTable, whereClause, orderByClause);

        var leftResult  = await _conn.ExecuteQueryAsync(leftSql);
        progress?.Report("載入右表資料...");
        var rightResult = await _conn.ExecuteQueryAsync(rightSql);

        if (!leftResult.Success)
            throw new Exception($"左表查詢失敗：{leftResult.ErrorMessage}");
        if (!rightResult.Success)
            throw new Exception($"右表查詢失敗：{rightResult.ErrorMessage}");

        var leftDt  = leftResult.Data!;
        var rightDt = rightResult.Data!;

        // 取兩表共同欄位
        var leftCols  = leftDt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var rightCols = rightDt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var commonCols = leftCols.Intersect(rightCols, StringComparer.OrdinalIgnoreCase).ToList();

        if (commonCols.Count == 0)
            throw new Exception("兩個資料表沒有共同欄位，無法比對。");

        // 自動偵測主鍵
        if (keyColumn == null)
            keyColumn = await DetectKeyColumnAsync(leftDb, leftTable, commonCols);

        progress?.Report("比對差異中...");
        var result = new DataDiffResult
        {
            Columns   = commonCols,
            KeyColumn = keyColumn
        };

        if (keyColumn != null)
            DiffByKey(leftDt, rightDt, commonCols, keyColumn, result);
        else
            DiffByPosition(leftDt, rightDt, commonCols, result);

        return result;
    }

    /// <summary>依主鍵比對（最準確）</summary>
    private static void DiffByKey(DataTable left, DataTable right,
        List<string> cols, string keyCol, DataDiffResult result)
    {
        // 建立 key → row 的字典
        var leftMap  = BuildRowMap(left,  keyCol, cols);
        var rightMap = BuildRowMap(right, keyCol, cols);

        var allKeys = leftMap.Keys.Union(rightMap.Keys).ToList();

        foreach (var key in allKeys)
        {
            var hasLeft  = leftMap.TryGetValue(key, out var leftVals);
            var hasRight = rightMap.TryGetValue(key, out var rightVals);

            if (hasLeft && !hasRight)
            {
                result.Rows.Add(new DiffRow
                {
                    Kind       = DiffRowKind.Deleted,
                    LeftValues = leftVals!,
                    RightValues= new()
                });
            }
            else if (!hasLeft && hasRight)
            {
                result.Rows.Add(new DiffRow
                {
                    Kind        = DiffRowKind.Added,
                    LeftValues  = new(),
                    RightValues = rightVals!
                });
            }
            else
            {
                var changed = FindChangedCols(leftVals!, rightVals!, cols);
                result.Rows.Add(new DiffRow
                {
                    Kind        = changed.Count > 0 ? DiffRowKind.Modified : DiffRowKind.Unchanged,
                    LeftValues  = leftVals!,
                    RightValues = rightVals!,
                    ChangedCols = changed
                });
            }
        }
    }

    /// <summary>依位置比對（無主鍵時用）</summary>
    private static void DiffByPosition(DataTable left, DataTable right,
        List<string> cols, DataDiffResult result)
    {
        int maxRows = Math.Max(left.Rows.Count, right.Rows.Count);
        for (int i = 0; i < maxRows; i++)
        {
            var leftVals  = i < left.Rows.Count  ? RowToDict(left.Rows[i],  cols) : new();
            var rightVals = i < right.Rows.Count ? RowToDict(right.Rows[i], cols) : new();

            DiffRowKind kind;
            HashSet<string> changed = new();

            if (i >= left.Rows.Count)       kind = DiffRowKind.Added;
            else if (i >= right.Rows.Count) kind = DiffRowKind.Deleted;
            else
            {
                changed = FindChangedCols(leftVals, rightVals, cols);
                kind = changed.Count > 0 ? DiffRowKind.Modified : DiffRowKind.Unchanged;
            }

            result.Rows.Add(new DiffRow
            {
                Kind        = kind,
                RowIndex    = i,
                LeftValues  = leftVals,
                RightValues = rightVals,
                ChangedCols = changed
            });
        }
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildRowMap(
        DataTable dt, string keyCol, List<string> cols)
    {
        var map = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (DataRow row in dt.Rows)
        {
            var key = row[keyCol]?.ToString() ?? "NULL";
            if (!map.ContainsKey(key))
                map[key] = RowToDict(row, cols);
        }
        return map;
    }

    private static Dictionary<string, object?> RowToDict(DataRow row, List<string> cols)
        => cols.Where(c => row.Table.Columns.Contains(c))
               .ToDictionary(c => c, c => row[c] == DBNull.Value ? null : row[c],
                             StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> FindChangedCols(
        Dictionary<string, object?> left,
        Dictionary<string, object?> right,
        List<string> cols)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in cols)
        {
            var lv = left.TryGetValue(col, out var l) ? l?.ToString() : null;
            var rv = right.TryGetValue(col, out var r) ? r?.ToString() : null;
            if (lv != rv) changed.Add(col);
        }
        return changed;
    }

    private async Task<string?> DetectKeyColumnAsync(string db, string table, List<string> cols)
    {
        try
        {
            var columns = await _conn.GetColumnsAsync(db, table);
            var pk = columns.FirstOrDefault(c => c.Key == "PRI");
            if (pk != null && cols.Contains(pk.Field, StringComparer.OrdinalIgnoreCase))
                return pk.Field;
            // 退而求其次：找名稱像 id 的欄位
            return cols.FirstOrDefault(c =>
                c.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                c.EndsWith("_id", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static string BuildSql(string db, string table,
        string? where, string? orderBy)
    {
        var sb = new StringBuilder();
        sb.Append($"SELECT * FROM `{db}`.`{table}`");
        if (!string.IsNullOrWhiteSpace(where))
            sb.Append($" WHERE {where}");
        if (!string.IsNullOrWhiteSpace(orderBy))
            sb.Append($" ORDER BY {orderBy}");
        return sb.ToString();
    }

    /// <summary>產生將右表同步到左表的 SQL patch</summary>
    public static string GeneratePatchSql(DataDiffResult result,
        string targetDb, string targetTable)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- 資料差異同步 SQL: {targetDb}.{targetTable}");
        sb.AppendLine($"-- 產生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var row in result.Rows)
        {
            switch (row.Kind)
            {
                case DiffRowKind.Added:
                    var cols = string.Join(", ", row.RightValues.Keys.Select(c => $"`{c}`"));
                    var vals = string.Join(", ", row.RightValues.Values.Select(v => EscapeValue(v)));
                    sb.AppendLine($"INSERT INTO `{targetDb}`.`{targetTable}` ({cols}) VALUES ({vals});");
                    break;

                case DiffRowKind.Deleted when result.KeyColumn != null:
                    var keyVal = EscapeValue(row.LeftValues.TryGetValue(result.KeyColumn, out var kv) ? kv : null);
                    sb.AppendLine($"DELETE FROM `{targetDb}`.`{targetTable}` WHERE `{result.KeyColumn}` = {keyVal};");
                    break;

                case DiffRowKind.Modified when result.KeyColumn != null:
                    var sets   = string.Join(", ",
                        row.ChangedCols.Select(c => $"`{c}` = {EscapeValue(row.RightValues.TryGetValue(c, out var v) ? v : null)}"));
                    var keyVal2 = EscapeValue(row.RightValues.TryGetValue(result.KeyColumn, out var kv2) ? kv2 : null);
                    sb.AppendLine($"UPDATE `{targetDb}`.`{targetTable}` SET {sets} WHERE `{result.KeyColumn}` = {keyVal2};");
                    break;
            }
        }
        return sb.ToString();
    }

    private static string EscapeValue(object? v)
    {
        if (v == null) return "NULL";
        if (v is bool b) return b ? "1" : "0";
        if (v is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
        if (double.TryParse(v.ToString(), out _)) return v.ToString()!;
        return $"'{v.ToString()!.Replace("'", "''")}'";
    }
}
