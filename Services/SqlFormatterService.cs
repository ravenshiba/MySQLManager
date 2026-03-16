using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MySQLManager.Services;

/// <summary>
/// SQL 格式化服務：將 SQL 美化排版
/// 支援 SELECT / INSERT / UPDATE / DELETE / CREATE TABLE
/// </summary>
public class SqlFormatOptions
{
    public bool   UppercaseKeywords { get; set; } = true;
    public int    IndentSize        { get; set; } = 4;
    public bool   NewlineBeforeAnd  { get; set; } = true;
    public bool   CompactJoins      { get; set; } = false;

    // Singleton with persistence
    private static SqlFormatOptions? _current;
    public static SqlFormatOptions Current
    {
        get
        {
            if (_current == null)
            {
                var path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MySQLManager", "format_options.json");
                try
                {
                    if (System.IO.File.Exists(path))
                        _current = System.Text.Json.JsonSerializer.Deserialize<SqlFormatOptions>(
                            System.IO.File.ReadAllText(path)) ?? new();
                }
                catch { }
                _current ??= new SqlFormatOptions();
            }
            return _current;
        }
    }

    public void Save()
    {
        var dir  = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MySQLManager");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "format_options.json");
        System.IO.File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        _current = this;
    }
}

public static class SqlFormatterService
{
    // 需要獨立一行的主要子句
    private static readonly string[] MainClauses =
    {
        "SELECT", "FROM", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "OUTER JOIN",
        "CROSS JOIN", "JOIN", "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "LIMIT", "OFFSET", "UNION ALL", "UNION", "INSERT INTO", "VALUES",
        "UPDATE", "SET", "DELETE FROM", "CREATE TABLE", "ALTER TABLE",
        "ON DUPLICATE KEY UPDATE", "WITH"
    };

    // 需要縮進的子句
    private static readonly HashSet<string> IndentedClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY", "LIMIT", "OFFSET",
        "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "OUTER JOIN", "CROSS JOIN", "JOIN",
        "VALUES", "SET", "ON DUPLICATE KEY UPDATE"
    };

    public static string Format(string sql, SqlFormatOptions? opts = null)
    {
        opts ??= SqlFormatOptions.Current;
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // 處理多語句（; 分隔）
        var statements = SplitStatements(sql);
        var formatted = new List<string>();
        foreach (var stmt in statements)
        {
            var trimmed = stmt.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                formatted.Add(FormatSingle(trimmed));
        }
        return string.Join("\n\n", formatted);
    }

    private static string FormatSingle(string sql)
    {
        sql = NormalizeWhitespace(sql);

        var upper = sql.ToUpperInvariant();

        // 根據第一個詞判斷類型
        if (upper.StartsWith("SELECT") || upper.StartsWith("WITH"))
            return FormatSelect(sql);
        if (upper.StartsWith("INSERT"))
            return FormatInsert(sql);
        if (upper.StartsWith("UPDATE"))
            return FormatUpdate(sql);
        if (upper.StartsWith("DELETE"))
            return FormatDelete(sql);
        if (upper.StartsWith("CREATE TABLE"))
            return FormatCreateTable(sql);
        return sql; // 其他類型原樣輸出
    }

    // ── SELECT ────────────────────────────────────────────────

    private static string FormatSelect(string sql)
    {
        var sb = new StringBuilder();
        var tokens = TokenizeByClause(sql);

        foreach (var (clause, body) in tokens)
        {
            var clauseUpper = clause.ToUpperInvariant().Trim();

            if (clauseUpper == "SELECT")
            {
                sb.AppendLine("SELECT");
                var cols = SplitByTopLevelComma(body.Trim());
                for (int i = 0; i < cols.Count; i++)
                    sb.AppendLine($"    {cols[i].Trim()}{(i < cols.Count - 1 ? "," : "")}");
            }
            else if (clauseUpper is "FROM" or "LEFT JOIN" or "RIGHT JOIN" or
                     "INNER JOIN" or "OUTER JOIN" or "CROSS JOIN" or "JOIN")
            {
                sb.AppendLine($"{clause.ToUpperInvariant().Trim()}");
                sb.AppendLine($"    {body.Trim()}");
            }
            else if (clauseUpper == "WHERE")
            {
                sb.AppendLine("WHERE");
                var conditions = SplitConditions(body.Trim());
                sb.AppendLine($"    {string.Join("\n    ", conditions)}");
            }
            else if (clauseUpper is "GROUP BY" or "ORDER BY")
            {
                sb.AppendLine($"{clauseUpper}");
                var items = SplitByTopLevelComma(body.Trim());
                for (int i = 0; i < items.Count; i++)
                    sb.AppendLine($"    {items[i].Trim()}{(i < items.Count - 1 ? "," : "")}");
            }
            else if (clauseUpper is "HAVING" or "LIMIT" or "OFFSET" or "UNION" or "UNION ALL")
            {
                sb.AppendLine($"{clauseUpper} {body.Trim()}");
            }
            else
            {
                sb.AppendLine($"{clause} {body}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ── INSERT ────────────────────────────────────────────────

    private static string FormatInsert(string sql)
    {
        var sb = new StringBuilder();
        // INSERT INTO table (cols) VALUES (...)
        var match = Regex.Match(sql,
            @"INSERT\s+(IGNORE\s+)?INTO\s+(\S+)\s*(\([^)]*\))?\s*VALUES\s*(.*)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success) return sql;

        var ignore = match.Groups[1].Value.Trim();
        var table  = match.Groups[2].Value;
        var cols   = match.Groups[3].Value;
        var vals   = match.Groups[4].Value.Trim();

        sb.AppendLine($"INSERT {ignore}INTO {table}");
        if (!string.IsNullOrEmpty(cols))
        {
            var colList = SplitByTopLevelComma(cols.Trim('(', ')'));
            sb.AppendLine("(");
            for (int i = 0; i < colList.Count; i++)
                sb.AppendLine($"    {colList[i].Trim()}{(i < colList.Count - 1 ? "," : "")}");
            sb.AppendLine(")");
        }
        sb.AppendLine("VALUES");

        // 多個 VALUES 組
        var valueGroups = Regex.Matches(vals, @"\(([^)]*)\)");
        for (int g = 0; g < valueGroups.Count; g++)
        {
            var items = SplitByTopLevelComma(valueGroups[g].Groups[1].Value);
            sb.Append("(");
            sb.Append(string.Join(", ", items));
            sb.Append(")");
            if (g < valueGroups.Count - 1) sb.AppendLine(",");
        }

        return sb.ToString().TrimEnd();
    }

    // ── UPDATE ────────────────────────────────────────────────

    private static string FormatUpdate(string sql)
    {
        var sb = new StringBuilder();
        var setMatch = Regex.Match(sql, @"UPDATE\s+(\S+)\s+SET\s+(.*?)(?:\s+WHERE\s+(.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!setMatch.Success) return sql;

        sb.AppendLine($"UPDATE {setMatch.Groups[1].Value}");
        sb.AppendLine("SET");
        var assignments = SplitByTopLevelComma(setMatch.Groups[2].Value.Trim());
        for (int i = 0; i < assignments.Count; i++)
            sb.AppendLine($"    {assignments[i].Trim()}{(i < assignments.Count - 1 ? "," : "")}");

        if (setMatch.Groups[3].Success && !string.IsNullOrWhiteSpace(setMatch.Groups[3].Value))
        {
            sb.AppendLine("WHERE");
            var conditions = SplitConditions(setMatch.Groups[3].Value.Trim());
            sb.AppendLine($"    {string.Join("\n    ", conditions)}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── DELETE ────────────────────────────────────────────────

    private static string FormatDelete(string sql)
    {
        var sb = new StringBuilder();
        var m = Regex.Match(sql, @"DELETE\s+FROM\s+(\S+)(?:\s+WHERE\s+(.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return sql;

        sb.AppendLine($"DELETE FROM {m.Groups[1].Value}");
        if (m.Groups[2].Success && !string.IsNullOrWhiteSpace(m.Groups[2].Value))
        {
            sb.AppendLine("WHERE");
            sb.Append($"    {m.Groups[2].Value.Trim()}");
        }
        return sb.ToString().TrimEnd();
    }

    // ── CREATE TABLE ──────────────────────────────────────────

    private static string FormatCreateTable(string sql)
    {
        var m = Regex.Match(sql, @"CREATE\s+TABLE\s+(\S+)\s*\((.*)\)\s*(.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return sql;

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {m.Groups[1].Value} (");
        var defs = SplitByTopLevelComma(m.Groups[2].Value);
        for (int i = 0; i < defs.Count; i++)
            sb.AppendLine($"    {defs[i].Trim()}{(i < defs.Count - 1 ? "," : "")}");
        sb.Append(")");
        if (!string.IsNullOrWhiteSpace(m.Groups[3].Value))
            sb.Append($" {m.Groups[3].Value.Trim()}");
        return sb.ToString().TrimEnd();
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string NormalizeWhitespace(string sql)
    {
        // 多個空白 → 單個空格（字串內容除外）
        return Regex.Replace(sql.Trim(), @"\s+", " ");
    }

    /// <summary>依主要子句切割 SQL，回傳 (clauseName, body) 列表</summary>
    private static List<(string Clause, string Body)> TokenizeByClause(string sql)
    {
        var result  = new List<(string, string)>();
        var pattern = string.Join("|",
            Array.ConvertAll(MainClauses, c =>
                @"\b" + Regex.Escape(c) + @"\b"));

        var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);
        if (matches.Count == 0) return new() { ("", sql) };

        for (int i = 0; i < matches.Count; i++)
        {
            var clauseName = matches[i].Value;
            var start = matches[i].Index + matches[i].Length;
            var end   = i + 1 < matches.Count ? matches[i + 1].Index : sql.Length;
            var body  = sql[start..end];
            result.Add((clauseName, body));
        }
        return result;
    }

    /// <summary>以頂層逗號分割（忽略括號內的逗號）</summary>
    private static List<string> SplitByTopLevelComma(string s)
    {
        var result = new List<string>();
        int depth = 0;
        var current = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch == '(' || ch == '[') depth++;
            else if (ch == ')' || ch == ']') depth--;
            else if (ch == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>分割 WHERE 條件（AND/OR 換行）</summary>
    private static List<string> SplitConditions(string s)
    {
        var result = new List<string>();
        // 頂層 AND / OR 前換行
        var parts = Regex.Split(s, @"\b(AND|OR)\b", RegexOptions.IgnoreCase);
        var current = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) // 條件片段
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.Append(parts[i].Trim());
            }
            else // AND / OR
            {
                if (current.Length > 0)
                    result.Add(current.ToString().Trim());
                current.Clear();
                current.Append(parts[i].ToUpperInvariant() + " ");
            }
        }
        if (current.Length > 0) result.Add(current.ToString().Trim());
        return result;
    }

    private static List<string> SplitStatements(string sql)
    {
        // 簡單以 ; 分割（忽略字串內的 ;）
        var result = new List<string>();
        int depth  = 0;
        bool inStr = false;
        char strChar = ' ';
        var current = new StringBuilder();

        for (int i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (!inStr && (ch == '\'' || ch == '"' || ch == '`'))
            {
                inStr = true; strChar = ch;
                current.Append(ch); continue;
            }
            if (inStr && ch == strChar && (i == 0 || sql[i-1] != '\\'))
            {
                inStr = false;
                current.Append(ch); continue;
            }
            if (!inStr && ch == '(') depth++;
            if (!inStr && ch == ')') depth--;
            if (!inStr && depth == 0 && ch == ';')
            {
                result.Add(current.ToString().Trim());
                current.Clear(); continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
            result.Add(current.ToString().Trim());
        return result;
    }
}
