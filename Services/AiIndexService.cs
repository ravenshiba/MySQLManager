using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySQLManager.Models;

namespace MySQLManager.Services;

public class IndexSuggestion
{
    public string  Table      { get; set; } = "";
    public string  Columns    { get; set; } = "";
    public string  Reason     { get; set; } = "";
    public string  Sql        { get; set; } = "";
    public string  ImpactIcon { get; set; } = "🟡";
    public string  Impact     { get; set; } = "中等";
    public double  Selectivity{ get; set; }
}

public class AiIndexService
{
    private readonly ConnectionService _conn;

    public AiIndexService(ConnectionService conn) => _conn = conn;

    public async Task<List<IndexSuggestion>> AnalyzeQueryAsync(string sql, string database)
    {
        var suggestions = new List<IndexSuggestion>();

        // 1. Run EXPLAIN
        var explain = await _conn.ExecuteQueryAsync($"EXPLAIN {sql}");
        if (explain.Data == null) return suggestions;

        foreach (System.Data.DataRow row in explain.Data.Rows)
        {
            var table    = row["table"]?.ToString()   ?? "";
            var type     = row["type"]?.ToString()    ?? "";
            var key      = row["key"]?.ToString()     ?? "";
            var extra    = row["Extra"]?.ToString()   ?? "";
            var possible = row["possible_keys"]?.ToString() ?? "";
            var colsUsed = row["key_len"]?.ToString() ?? "";
            var rows_est = row["rows"]?.ToString()    ?? "0";
            long.TryParse(rows_est, out var rowsEst);

            if (string.IsNullOrEmpty(table)) continue;

            // Full table scan
            if ((type == "ALL" || type == "index") && rowsEst > 100)
            {
                var whereCol = ExtractWhereColumns(sql, table);
                if (whereCol.Count > 0)
                {
                    var cols = string.Join(", ", whereCol.Select(c => $"`{c}`"));
                    var idxName = "idx_" + string.Join("_", whereCol);
                    var impact = rowsEst > 10000 ? "高" : rowsEst > 1000 ? "中等" : "低";
                    var icon   = rowsEst > 10000 ? "🔴" : rowsEst > 1000  ? "🟡"   : "🟢";
                    suggestions.Add(new IndexSuggestion
                    {
                        Table      = table,
                        Columns    = string.Join(", ", whereCol),
                        Reason     = $"全表掃描（{rowsEst:N0} 行），WHERE 條件欄位無索引",
                        Sql        = $"CREATE INDEX `{idxName}` ON `{database}`.`{table}` ({cols});",
                        Impact     = impact,
                        ImpactIcon = icon,
                    });
                }
            }

            // Using filesort
            if (extra.Contains("Using filesort"))
            {
                var orderCol = ExtractOrderByColumns(sql);
                if (orderCol.Count > 0)
                {
                    var cols = string.Join(", ", orderCol.Select(c => $"`{c}`"));
                    suggestions.Add(new IndexSuggestion
                    {
                        Table      = table,
                        Columns    = string.Join(", ", orderCol),
                        Reason     = "ORDER BY 使用了 filesort，缺少排序索引",
                        Sql        = $"CREATE INDEX `idx_{string.Join("_", orderCol)}` ON `{database}`.`{table}` ({cols});",
                        Impact     = "中等",
                        ImpactIcon = "🟡",
                    });
                }
            }

            // Using temporary
            if (extra.Contains("Using temporary"))
            {
                var groupCol = ExtractGroupByColumns(sql);
                if (groupCol.Count > 0)
                {
                    var cols = string.Join(", ", groupCol.Select(c => $"`{c}`"));
                    suggestions.Add(new IndexSuggestion
                    {
                        Table      = table,
                        Columns    = string.Join(", ", groupCol),
                        Reason     = "GROUP BY 使用了臨時表，建議加入 GROUP BY 欄位索引",
                        Sql        = $"CREATE INDEX `idx_gb_{string.Join("_", groupCol)}` ON `{database}`.`{table}` ({cols});",
                        Impact     = "高",
                        ImpactIcon = "🔴",
                    });
                }
            }
        }

        // 2. Check for duplicate indexes
        var dupSuggestions = await CheckDuplicateIndexesAsync(database);
        suggestions.AddRange(dupSuggestions);

        return suggestions.DistinctBy(s => s.Sql).ToList();
    }

    private async Task<List<IndexSuggestion>> CheckDuplicateIndexesAsync(string database)
    {
        var result = new List<IndexSuggestion>();
        var sql = $@"
SELECT TABLE_NAME, INDEX_NAME, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS cols
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = '{database}' AND INDEX_NAME != 'PRIMARY'
GROUP BY TABLE_NAME, INDEX_NAME
ORDER BY TABLE_NAME, cols";

        var r = await _conn.ExecuteQueryAsync(sql);
        if (r.Data == null) return result;

        // Group rows by TABLE_NAME manually (avoids System.Data.DataSetExtensions)
        var grouped = new Dictionary<string, List<System.Data.DataRow>>();
        foreach (System.Data.DataRow row in r.Data.Rows)
        {
            var tbl = row["TABLE_NAME"]?.ToString() ?? "";
            if (!grouped.ContainsKey(tbl)) grouped[tbl] = new();
            grouped[tbl].Add(row);
        }

        foreach (var (tableName, rows) in grouped)
        {
            var idxList = rows
                .Select(row => (
                    Name: row["INDEX_NAME"]?.ToString() ?? "",
                    Cols: row["cols"]?.ToString() ?? ""))
                .ToList();

            // Find prefixes (idx on (a,b) makes idx on (a) redundant)
            for (int i = 0; i < idxList.Count; i++)
            for (int j = 0; j < idxList.Count; j++)
            {
                if (i == j) continue;
                if (idxList[j].Cols.StartsWith(idxList[i].Cols + ",") ||
                    idxList[j].Cols == idxList[i].Cols)
                {
                    result.Add(new IndexSuggestion
                    {
                        Table      = tableName,
                        Columns    = idxList[i].Cols,
                        Reason     = $"索引 `{idxList[i].Name}` 是 `{idxList[j].Name}` 的前綴，可能是多餘索引",
                        Sql        = $"-- 考慮刪除：DROP INDEX `{idxList[i].Name}` ON `{database}`.`{tableName}`;",
                        Impact     = "低",
                        ImpactIcon = "🟢",
                    });
                }
            }
        }
        return result;
    }

    // ── SQL parsing helpers ───────────────────────────────────────────────
    private static List<string> ExtractWhereColumns(string sql, string table)
    {
        var cols = new List<string>();
        var m = System.Text.RegularExpressions.Regex.Matches(sql,
            @"WHERE\s+(.+?)(?:\s+(?:ORDER|GROUP|HAVING|LIMIT|$))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Singleline);
        foreach (System.Text.RegularExpressions.Match match in m)
        {
            var where = match.Groups[1].Value;
            var colMatches = System.Text.RegularExpressions.Regex.Matches(where,
                @"(?:`?(\w+)`?\.)?`?(\w+)`?\s*[=<>!]");
            foreach (System.Text.RegularExpressions.Match cm in colMatches)
            {
                var col = cm.Groups[2].Value;
                if (!string.IsNullOrEmpty(col) && col != "NULL" && !cols.Contains(col))
                    cols.Add(col);
            }
        }
        return cols.Take(3).ToList();
    }

    private static List<string> ExtractOrderByColumns(string sql)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sql,
            @"ORDER\s+BY\s+(.+?)(?:\s+(?:LIMIT|$))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return new();
        return m.Groups[1].Value.Split(',')
            .Select(c => System.Text.RegularExpressions.Regex.Replace(c.Trim(),
                @"\s+(ASC|DESC)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim('`'))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Take(3).ToList();
    }

    private static List<string> ExtractGroupByColumns(string sql)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sql,
            @"GROUP\s+BY\s+(.+?)(?:\s+(?:HAVING|ORDER|LIMIT|$))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return new();
        return m.Groups[1].Value.Split(',')
            .Select(c => c.Trim().Trim('`'))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Take(3).ToList();
    }
}
