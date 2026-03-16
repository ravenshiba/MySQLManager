using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MySQLManager.Services;

public class CompletionItem
{
    public string Text        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CompletionKind Kind { get; set; }
    public int    Priority    { get; set; } = 50;
    public string Icon => Kind switch
    {
        CompletionKind.Keyword  => "🔵",
        CompletionKind.Table    => "📋",
        CompletionKind.Column   => "📌",
        CompletionKind.Database => "🗄️",
        CompletionKind.Function => "⚡",
        CompletionKind.Alias    => "🏷️",
        CompletionKind.Snippet  => "📄",
        _                       => "📄"
    };
}

public enum CompletionKind { Keyword, Table, Column, Database, Function, Alias, Snippet }

public class SqlAutoCompleteService
{
    private readonly ConnectionService _conn;
    private readonly Dictionary<string, List<string>>     _tableCache  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ColumnMeta>> _columnCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string FromCol, string ToTable, string ToCol)>> _fkCache = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _databases = new();
    private bool _initialized;

    public SqlAutoCompleteService(ConnectionService conn) => _conn = conn;

    // ── Schema 快取 ───────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (!_conn.IsConnected) return;
        try { _databases = await _conn.GetDatabasesAsync(); _initialized = true; }
        catch { }
    }

    public async Task LoadTablesAsync(string database)
    {
        if (_tableCache.ContainsKey(database)) return;
        try { _tableCache[database] = await _conn.GetTablesAsync(database); }
        catch { }
    }

    public async Task LoadColumnsAsync(string database, string table)
    {
        var key = $"{database}.{table}";
        if (_columnCache.ContainsKey(key)) return;
        try
        {
            var cols = await _conn.GetColumnsAsync(database, table);
            _columnCache[key] = cols.Select(c => new ColumnMeta
            {
                Name     = c.Field,
                Type     = c.Type,
                IsPK     = c.Key == "PRI",
                IsFK     = c.Key == "MUL",
                Nullable = c.Null == "YES"
            }).ToList();
        }
        catch { }
    }

    public void InvalidateCache(string? database = null)
    {
        if (database == null) { _tableCache.Clear(); _columnCache.Clear(); }
        else
        {
            _tableCache.Remove(database);
            foreach (var k in _columnCache.Keys
                .Where(k => k.StartsWith(database + ".", StringComparison.OrdinalIgnoreCase))
                .ToList())
                _columnCache.Remove(k);
        }
    }

    // ── 主要補全入口 ──────────────────────────────────────────

    public async Task<List<CompletionItem>> GetCompletionsAsync(
        string textBeforeCursor, string? currentDatabase = null)
    {
        if (!_initialized) await InitializeAsync();

        var result = new List<CompletionItem>();
        var word   = GetCurrentWord(textBeforeCursor);
        var upper  = textBeforeCursor.ToUpperInvariant();

        if (word.Length == 0 && !EndsWithTrigger(textBeforeCursor))
            return result;

        var ctx   = ParseContext(upper, currentDatabase);
        var token = GetContextToken(upper);

        // 預載欄位資訊
        if (currentDatabase != null)
        {
            await LoadTablesAsync(currentDatabase);
            foreach (var tbl in ctx.Tables)
                await LoadColumnsAsync(currentDatabase, tbl);
        }

        switch (token.Type)
        {
            case TokenType.AfterDot:
                await AddDotCompletions(result, textBeforeCursor, currentDatabase, ctx);
                return result;

            case TokenType.AfterFrom:
            case TokenType.AfterJoin:
            case TokenType.AfterInto:
            case TokenType.AfterUpdate:
                await AddTableCompletions(result, word, currentDatabase, ctx);
                if (token.Type == TokenType.AfterJoin)
                    AddJoinTypeSnippets(result, word);
                break;

            case TokenType.AfterOn:
                await AddJoinOnCompletions(result, word, currentDatabase, ctx);
                break;

            case TokenType.AfterSelect:
            case TokenType.AfterWhere:
            case TokenType.AfterSet:
            case TokenType.AfterHaving:
            case TokenType.AfterOrderBy:
            case TokenType.AfterGroupBy:
                await AddColumnCompletions(result, word, currentDatabase, ctx);
                AddAliasCompletions(result, word, ctx);
                break;

            case TokenType.AfterUse:
            case TokenType.AfterDatabase:
                AddDatabaseCompletions(result, word);
                break;

            default:
                await AddColumnCompletions(result, word, currentDatabase, ctx);
                await AddTableCompletions(result, word, currentDatabase, ctx);
                AddAliasCompletions(result, word, ctx);
                break;
        }

        AddKeywordCompletions(result, word);
        AddFunctionCompletions(result, word);
        if (word.Length >= 2)
            AddSnippetCompletions(result, word, ctx);

        return result
            .DistinctBy(r => r.Text.ToUpperInvariant())
            .OrderBy(r => r.Priority).ThenBy(r => r.Text)
            .Take(40).ToList();
    }

    // ── 各類補全 ──────────────────────────────────────────────

    private Task AddTableCompletions(List<CompletionItem> result, string word,
        string? db, SqlContext ctx)
    {
        if (db != null && _tableCache.TryGetValue(db, out var tbls))
            result.AddRange(tbls
                .Where(t => t.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                .Select(t => new CompletionItem { Text = t, Kind = CompletionKind.Table,
                    Description = $"資料表 · {db}", Priority = 10 }));

        result.AddRange(_databases
            .Where(d => d.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Select(d => new CompletionItem { Text = d, Kind = CompletionKind.Database,
                Description = "資料庫", Priority = 20 }));
        return Task.CompletedTask;
    }

    private Task AddColumnCompletions(List<CompletionItem> result, string word,
        string? db, SqlContext ctx)
    {
        if (db == null) return Task.CompletedTask;
        foreach (var tbl in ctx.Tables)
        {
            var key = $"{db}.{tbl}";
            if (!_columnCache.TryGetValue(key, out var cols)) continue;
            result.AddRange(cols
                .Where(c => c.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                .Select(c => new CompletionItem
                {
                    Text = c.Name, Kind = CompletionKind.Column,
                    Description = $"{c.TypeLabel}  ·  {tbl}{(c.IsPK ? "  🔑" : "")}{(c.IsFK ? "  🔗" : "")}",
                    Priority = c.IsPK ? 5 : 15
                }));
        }
        return Task.CompletedTask;
    }

    private async Task AddDotCompletions(List<CompletionItem> result,
        string textBeforeCursor, string? db, SqlContext ctx)
    {
        var dotIdx = textBeforeCursor.LastIndexOf('.');
        if (dotIdx <= 0) return;
        var prefix = GetWordBefore(textBeforeCursor, dotIdx);
        if (string.IsNullOrEmpty(prefix)) return;

        // 別名展開
        if (ctx.Aliases.TryGetValue(prefix, out var aliasTarget) && db != null)
        {
            await LoadColumnsAsync(db, aliasTarget);
            var key = $"{db}.{aliasTarget}";
            if (_columnCache.TryGetValue(key, out var cols))
                result.AddRange(cols.Select(c => new CompletionItem
                {
                    Text = $"{prefix}.{c.Name}", Kind = CompletionKind.Column,
                    Description = $"{c.TypeLabel}  ·  {aliasTarget}{(c.IsPK ? "  🔑" : "")}{(c.IsFK ? "  🔗" : "")}",
                    Priority = c.IsPK ? 3 : 8
                }));
            return;
        }

        // 資料表展開
        if (db != null && _tableCache.TryGetValue(db, out var dbTables)
            && dbTables.Any(t => t.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await LoadColumnsAsync(db, prefix.ToLowerInvariant());
            var key = $"{db}.{prefix.ToLowerInvariant()}";
            if (_columnCache.TryGetValue(key, out var cols))
                result.AddRange(cols.Select(c => new CompletionItem
                {
                    Text = $"{prefix}.{c.Name}", Kind = CompletionKind.Column,
                    Description = $"{c.TypeLabel}  ·  {prefix}{(c.IsPK ? "  🔑" : "")}{(c.IsFK ? "  🔗" : "")}",
                    Priority = c.IsPK ? 3 : 8
                }));
            return;
        }

        // 資料庫展開
        await LoadTablesAsync(prefix);
        if (_tableCache.TryGetValue(prefix, out var dbtbls))
            result.AddRange(dbtbls.Select(t => new CompletionItem
            {
                Text = $"{prefix}.{t}", Kind = CompletionKind.Table,
                Description = $"資料表 · {prefix}", Priority = 10
            }));
    }

    private Task AddJoinOnCompletions(List<CompletionItem> result, string word,
        string? db, SqlContext ctx)
    {
        if (db == null) return Task.CompletedTask;
        foreach (var tbl in ctx.Tables)
        {
            var key = $"{db}.{tbl}";
            if (!_columnCache.TryGetValue(key, out var cols)) continue;
            // 優先 PK/FK，其次符合 word 的
            var candidates = cols.Where(c =>
                c.IsPK || c.IsFK ||
                (word.Length > 0 && c.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
            foreach (var col in candidates)
            {
                result.Add(new CompletionItem
                {
                    Text = $"{tbl}.{col.Name}", Kind = CompletionKind.Column,
                    Description = $"{col.TypeLabel}  ·  {tbl}{(col.IsPK ? "  🔑" : "")}{(col.IsFK ? "  🔗" : "")}",
                    Priority = col.IsPK ? 3 : col.IsFK ? 5 : 15
                });
                // 也加別名版本
                foreach (var kv in ctx.Aliases.Where(a => a.Value.Equals(tbl, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new CompletionItem
                    {
                        Text = $"{kv.Key}.{col.Name}", Kind = CompletionKind.Column,
                        Description = $"{col.TypeLabel}  ·  {tbl} [{kv.Key}]{(col.IsPK ? "  🔑" : "")}",
                        Priority = col.IsPK ? 4 : col.IsFK ? 6 : 16
                    });
            }
        }
        // 自動猜測 JOIN 條件
        AddAutoJoinSnippets(result, db, ctx);
        return Task.CompletedTask;
    }

    private async Task LoadFkCacheAsync(string db)
    {
        if (_fkCache.ContainsKey(db)) return;
        _fkCache[db] = new(); // mark as loading
        try
        {
            var sql = $@"SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = '{db}' AND REFERENCED_TABLE_NAME IS NOT NULL";
            var r = await _conn.ExecuteQueryAsync(sql);
            if (r.Data == null) return;
            foreach (System.Data.DataRow row in r.Data.Rows)
            {
                var tbl = row["TABLE_NAME"]?.ToString() ?? "";
                var key = $"{db}.{tbl}";
                if (!_fkCache.ContainsKey(key)) _fkCache[key] = new();
                _fkCache[key].Add((
                    row["COLUMN_NAME"]?.ToString() ?? "",
                    row["REFERENCED_TABLE_NAME"]?.ToString() ?? "",
                    row["REFERENCED_COLUMN_NAME"]?.ToString() ?? ""));
            }
        }
        catch { }
    }

    private IEnumerable<(string, string, string, string)> GetFkPairs(string db, string t1, string t2)
    {
        var key1 = $"{db}.{t1}";
        var key2 = $"{db}.{t2}";
        if (_fkCache.TryGetValue(key1, out var fks1))
            foreach (var (fromCol, toTable, toCol) in fks1)
                if (toTable.Equals(t2, StringComparison.OrdinalIgnoreCase))
                    yield return (t1, fromCol, t2, toCol);
        if (_fkCache.TryGetValue(key2, out var fks2))
            foreach (var (fromCol, toTable, toCol) in fks2)
                if (toTable.Equals(t1, StringComparison.OrdinalIgnoreCase))
                    yield return (t2, fromCol, t1, toCol);
    }

    private void AddAutoJoinSnippets(List<CompletionItem> result, string db, SqlContext ctx)
    {
        _ = LoadFkCacheAsync(db); // kick off FK loading for next time
        for (int i = 0; i < ctx.Tables.Count - 1; i++)
        {
            var t1 = ctx.Tables[i];
            var t2 = ctx.Tables[i + 1];

            // Real FK hints from information_schema
            foreach (var (fromTable, fromCol, toTable, toCol) in GetFkPairs(db, t1, t2))
                result.Insert(0, new CompletionItem {
                    Text = $"{fromTable}.{fromCol} = {toTable}.{toCol}",
                    Kind = CompletionKind.Snippet,
                    Description = $"🔑 FK：{fromTable}.{fromCol} → {toTable}.{toCol}",
                    Priority = 1 });

            if (!_columnCache.TryGetValue($"{db}.{t1}", out var c1) ||
                !_columnCache.TryGetValue($"{db}.{t2}", out var c2)) continue;

            // 猜測 t2.t1_id = t1.id
            var fkName = $"{t1}_id";
            var pkCol  = c1.FirstOrDefault(c => c.IsPK);
            if (pkCol != null && c2.Any(c => c.Name.Equals(fkName, StringComparison.OrdinalIgnoreCase)))
                result.Add(new CompletionItem
                {
                    Text = $"{t1}.{pkCol.Name} = {t2}.{fkName}",
                    Kind = CompletionKind.Snippet,
                    Description = $"🔗 猜測關聯 {t1}→{t2}", Priority = 2
                });

            // 反向猜測 t1.t2_id = t2.id
            fkName = $"{t2}_id";
            pkCol  = c2.FirstOrDefault(c => c.IsPK);
            if (pkCol != null && c1.Any(c => c.Name.Equals(fkName, StringComparison.OrdinalIgnoreCase)))
                result.Add(new CompletionItem
                {
                    Text = $"{t2}.{pkCol.Name} = {t1}.{fkName}",
                    Kind = CompletionKind.Snippet,
                    Description = $"🔗 猜測關聯 {t2}→{t1}", Priority = 2
                });

            // 共同欄位
            var skipCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "created_at","updated_at","deleted_at","status","name","description","remark" };
            var common = c1.Select(c => c.Name)
                           .Intersect(c2.Select(c => c.Name), StringComparer.OrdinalIgnoreCase)
                           .Where(n => !skipCols.Contains(n));
            foreach (var col in common)
                result.Add(new CompletionItem
                {
                    Text = $"{t1}.{col} = {t2}.{col}",
                    Kind = CompletionKind.Snippet,
                    Description = $"共同欄位：{col}", Priority = 7
                });
        }
    }

    private void AddJoinTypeSnippets(List<CompletionItem> result, string word)
    {
        var types = new[]
        {
            ("LEFT JOIN",  "保留左表所有列"),
            ("RIGHT JOIN", "保留右表所有列"),
            ("INNER JOIN", "只保留匹配列"),
            ("CROSS JOIN", "笛卡爾積"),
        };
        foreach (var (kw, desc) in types)
            if (word.Length == 0 || kw.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                result.Add(new CompletionItem
                    { Text = kw, Kind = CompletionKind.Keyword, Description = desc, Priority = 8 });
    }

    private void AddAliasCompletions(List<CompletionItem> result, string word, SqlContext ctx)
    {
        result.AddRange(ctx.Aliases.Keys
            .Where(a => a.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Select(a => new CompletionItem
            {
                Text = a, Kind = CompletionKind.Alias,
                Description = $"別名 → {ctx.Aliases[a]}", Priority = 12
            }));
    }

    private void AddDatabaseCompletions(List<CompletionItem> result, string word)
    {
        result.AddRange(_databases
            .Where(d => d.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Select(d => new CompletionItem
                { Text = d, Kind = CompletionKind.Database, Description = "資料庫", Priority = 10 }));
    }

    private void AddKeywordCompletions(List<CompletionItem> result, string word)
    {
        if (word.Length == 0) return;
        result.AddRange(Keywords
            .Where(k => k.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Select(k => new CompletionItem
                { Text = k, Kind = CompletionKind.Keyword, Description = "SQL 關鍵字", Priority = 30 }));
    }

    private void AddFunctionCompletions(List<CompletionItem> result, string word)
    {
        if (word.Length == 0) return;
        result.AddRange(Functions
            .Where(f => f.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Select(f => new CompletionItem
            {
                Text = f, Kind = CompletionKind.Function,
                Description = FunctionDesc.TryGetValue(f, out var d) ? d : "SQL 函式",
                Priority = 25
            }));
    }

    private void AddSnippetCompletions(List<CompletionItem> result, string word, SqlContext ctx)
    {
        var dynamic = ctx.Tables.Take(3)
            .SelectMany(t => new[]
            {
                ($"SELECT * FROM {t} WHERE ", $"快速查詢 {t}"),
                ($"SELECT COUNT(*) FROM {t}", $"計算 {t} 筆數"),
            });

        foreach (var (text, desc) in StaticSnippets.Select(s => (s, "SQL 範本")).Concat(dynamic))
            if (text.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                result.Add(new CompletionItem
                    { Text = text, Kind = CompletionKind.Snippet, Description = desc, Priority = 35 });
    }

    // ── 上下文解析 ────────────────────────────────────────────

    private SqlContext ParseContext(string upperSql, string? currentDb)
    {
        var ctx     = new SqlContext { CurrentDatabase = currentDb };
        var cleaned = RemoveStringLiterals(upperSql);
        var flat    = FlattenSubqueries(cleaned, ctx);
        ExtractTablesAndAliases(flat, ctx);
        var useMatch = Regex.Match(cleaned, @"\bUSE\s+(\w+)", RegexOptions.IgnoreCase);
        if (useMatch.Success) ctx.Database = useMatch.Groups[1].Value.ToLowerInvariant();
        return ctx;
    }

    private static string RemoveStringLiterals(string sql)
        => Regex.Replace(sql, @"'[^']*'|""[^""]*""|`[^`]*`", "''");

    private static string FlattenSubqueries(string sql, SqlContext ctx)
        => Regex.Replace(sql,
            @"\(\s*SELECT\b[^()]*(?:\([^()]*\)[^()]*)*\)\s*(?:AS\s+)?(\w+)?",
            m =>
            {
                var alias = m.Groups[1].Success ? m.Groups[1].Value : $"__sub{ctx.SubqueryCount++}";
                ctx.SubqueryAliases.Add(alias.ToLowerInvariant());
                return alias;
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static void ExtractTablesAndAliases(string sql, SqlContext ctx)
    {
        var pattern = new Regex(
            @"(?:FROM|JOIN)\s+([`\w]+(?:\.[`\w]+)?)\s*(?:AS\s+)?(\w+)?(?=\s*(?:,|\b(?:WHERE|ON|JOIN|LEFT|RIGHT|INNER|CROSS|GROUP|ORDER|HAVING|LIMIT|SET|$)\b))",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in pattern.Matches(sql))
        {
            var rawTable = m.Groups[1].Value.Trim('`');
            var table    = rawTable.Contains('.') ? rawTable.Split('.').Last() : rawTable;
            table = table.ToLowerInvariant();
            if (!string.IsNullOrEmpty(table) && !ctx.SubqueryAliases.Contains(table)
                && !ctx.Tables.Contains(table))
                ctx.Tables.Add(table);

            if (m.Groups[2].Success)
            {
                var alias = m.Groups[2].Value.ToLowerInvariant();
                if (!ReservedWords.Contains(alias.ToUpperInvariant()))
                    ctx.Aliases[alias] = table;
            }
        }

        // 逗號分隔多表
        var commaMatch = Regex.Match(sql,
            @"FROM\s+(.+?)(?=\b(?:WHERE|GROUP|ORDER|HAVING|LIMIT|JOIN|LEFT|RIGHT|INNER|CROSS)\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!commaMatch.Success) return;
        foreach (var part in commaMatch.Groups[1].Value.Split(','))
        {
            var tokens = part.Trim().Split(new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var tbl = tokens[0].Trim('`');
            if (tbl.Contains('.')) tbl = tbl.Split('.').Last();
            tbl = tbl.ToLowerInvariant();
            if (!string.IsNullOrEmpty(tbl) && !ReservedWords.Contains(tbl.ToUpperInvariant())
                && !ctx.Tables.Contains(tbl))
                ctx.Tables.Add(tbl);
            if (tokens.Length < 2) continue;
            var ai = tokens.Length >= 3 && tokens[1].Equals("AS", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            if (ai < tokens.Length)
            {
                var alias = tokens[ai].ToLowerInvariant();
                if (!ReservedWords.Contains(alias.ToUpperInvariant()) && alias != tbl)
                    ctx.Aliases[alias] = tbl;
            }
        }
    }

    // ── Token 分析 ────────────────────────────────────────────

    private static ContextToken GetContextToken(string upper)
    {
        if (upper.TrimEnd().EndsWith('.'))
            return new ContextToken { Type = TokenType.AfterDot };

        var word      = GetCurrentWord(upper);
        var beforeWord = upper[..^word.Length].TrimEnd();

        var checks = new (string kw, TokenType type)[]
        {
            ("INSERT INTO", TokenType.AfterInto),
            ("LEFT JOIN",   TokenType.AfterJoin),
            ("RIGHT JOIN",  TokenType.AfterJoin),
            ("INNER JOIN",  TokenType.AfterJoin),
            ("CROSS JOIN",  TokenType.AfterJoin),
            ("JOIN",        TokenType.AfterJoin),
            ("ORDER BY",    TokenType.AfterOrderBy),
            ("GROUP BY",    TokenType.AfterGroupBy),
            ("SELECT",      TokenType.AfterSelect),
            ("FROM",        TokenType.AfterFrom),
            ("WHERE",       TokenType.AfterWhere),
            ("ON",          TokenType.AfterOn),
            ("SET",         TokenType.AfterSet),
            ("HAVING",      TokenType.AfterHaving),
            ("INTO",        TokenType.AfterInto),
            ("UPDATE",      TokenType.AfterUpdate),
            ("USE",         TokenType.AfterUse),
            ("DATABASE",    TokenType.AfterDatabase),
        };

        int bestIdx = -1; var bestType = TokenType.Unknown;
        foreach (var (kw, type) in checks)
        {
            var idx = beforeWord.LastIndexOf(kw, StringComparison.Ordinal);
            if (idx < 0) continue;
            bool startOk = idx == 0 || !char.IsLetterOrDigit(beforeWord[idx - 1]);
            bool endOk   = idx + kw.Length >= beforeWord.Length ||
                           !char.IsLetterOrDigit(beforeWord[idx + kw.Length]);
            if (startOk && endOk && idx > bestIdx)
            { bestIdx = idx; bestType = type; }
        }
        return new ContextToken { Type = bestType };
    }

    // ── 工具 ──────────────────────────────────────────────────

    private static string GetCurrentWord(string text)
    {
        var i = text.Length - 1;
        if (i >= 0 && text[i] == '.') return "";
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i--;
        return text[(i + 1)..];
    }

    private static string GetWordBefore(string text, int pos)
    {
        var i = pos - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i--;
        return text[(i + 1)..pos];
    }

    private static bool EndsWithTrigger(string text)
        => text.Length > 0 && (text[^1] == '.' || text[^1] == ' ' || text[^1] == '\t');

    // ── 靜態資料 ──────────────────────────────────────────────

    private static readonly string[] Keywords =
    {
        "SELECT","FROM","WHERE","AND","OR","NOT","IN","EXISTS","BETWEEN","LIKE",
        "INSERT","INTO","VALUES","UPDATE","SET","DELETE","TRUNCATE",
        "CREATE","TABLE","DATABASE","INDEX","VIEW","TRIGGER","PROCEDURE","FUNCTION",
        "DROP","ALTER","ADD","MODIFY","CHANGE","RENAME",
        "JOIN","LEFT JOIN","RIGHT JOIN","INNER JOIN","OUTER JOIN","CROSS JOIN",
        "ON","USING",
        "GROUP BY","ORDER BY","HAVING","LIMIT","OFFSET",
        "DISTINCT","ALL","AS","IS","NULL","NOT NULL",
        "ASC","DESC",
        "PRIMARY KEY","FOREIGN KEY","UNIQUE","DEFAULT","AUTO_INCREMENT",
        "SHOW","DESCRIBE","EXPLAIN","USE","CALL",
        "BEGIN","COMMIT","ROLLBACK","TRANSACTION","SAVEPOINT",
        "IF","THEN","ELSE","END","CASE","WHEN","ELSEIF",
        "TRUE","FALSE","CURRENT_TIMESTAMP",
        "UNION","UNION ALL","WITH","RECURSIVE","OVER","PARTITION BY",
    };

    private static readonly string[] Functions =
    {
        "COUNT(","SUM(","AVG(","MIN(","MAX(",
        "COALESCE(","IFNULL(","NULLIF(","IF(",
        "CONCAT(","CONCAT_WS(","SUBSTRING(","LEFT(","RIGHT(",
        "LENGTH(","CHAR_LENGTH(","TRIM(","UPPER(","LOWER(","REPLACE(",
        "LPAD(","RPAD(","FORMAT(","LOCATE(",
        "NOW()","CURDATE()","CURTIME()",
        "DATE(","YEAR(","MONTH(","DAY(","HOUR(","MINUTE(","SECOND(",
        "DATE_FORMAT(","DATEDIFF(","DATE_ADD(","DATE_SUB(","TIMESTAMPDIFF(",
        "UNIX_TIMESTAMP(","FROM_UNIXTIME(",
        "ROUND(","FLOOR(","CEIL(","ABS(","MOD(","POWER(",
        "RAND()","CAST(","CONVERT(",
        "GROUP_CONCAT(","JSON_EXTRACT(","JSON_OBJECT(","JSON_ARRAY(","JSON_UNQUOTE(",
        "ROW_NUMBER()","RANK()","DENSE_RANK()","LAG(","LEAD(","OVER(",
        "PARTITION BY",
    };

    private static readonly Dictionary<string, string> FunctionDesc = new()
    {
        ["COUNT("]       = "計算筆數",
        ["SUM("]         = "加總",
        ["AVG("]         = "平均值",
        ["COALESCE("]    = "回傳第一個非 NULL 值",
        ["IFNULL("]      = "若為 NULL 回傳預設值",
        ["CONCAT("]      = "字串串接",
        ["DATE_FORMAT("] = "日期格式化",
        ["GROUP_CONCAT("]= "群組字串合併",
        ["ROW_NUMBER()"] = "視窗函式：行號",
        ["RANK()"]       = "視窗函式：排名（有跳號）",
        ["DENSE_RANK()"] = "視窗函式：排名（無跳號）",
        ["JSON_EXTRACT("]= "提取 JSON 欄位值",
        ["DATEDIFF("]    = "兩日期差距（天）",
        ["TIMESTAMPDIFF("]="兩時間差距",
        ["CAST("]        = "型別轉換",
    };

    private static readonly string[] StaticSnippets =
    {
        "SELECT * FROM",
        "SELECT COUNT(*) FROM",
        "SELECT DISTINCT",
        "INSERT INTO  (columns) VALUES (values)",
        "UPDATE  SET  WHERE id = ",
        "DELETE FROM  WHERE id = ",
        "CREATE TABLE  (\n  id INT UNSIGNED NOT NULL AUTO_INCREMENT,\n  created_at DATETIME DEFAULT CURRENT_TIMESTAMP,\n  PRIMARY KEY (id)\n)",
        "ALTER TABLE  ADD COLUMN  VARCHAR(255)",
        "ALTER TABLE  MODIFY COLUMN",
        "SHOW TABLES",
        "SHOW DATABASES",
        "SHOW FULL COLUMNS FROM",
        "DESCRIBE ",
        "EXPLAIN SELECT",
        "WITH cte AS (\n  SELECT \n  FROM \n)\nSELECT * FROM cte",
        "SELECT\n    a.*,\n    b.*\nFROM  a\nLEFT JOIN  b ON a.id = b.a_id\nWHERE ",
        "SELECT column, COUNT(*) AS cnt\nFROM \nGROUP BY column\nHAVING cnt > 1\nORDER BY cnt DESC",
    };

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT","FROM","WHERE","AND","OR","NOT","IN","EXISTS",
        "INSERT","INTO","VALUES","UPDATE","SET","DELETE",
        "CREATE","TABLE","DATABASE","INDEX","VIEW","DROP","ALTER",
        "JOIN","LEFT","RIGHT","INNER","OUTER","CROSS","ON","USING",
        "GROUP","ORDER","BY","HAVING","LIMIT","OFFSET","AS",
        "IS","NULL","TRUE","FALSE","DISTINCT","ALL","ASC","DESC",
        "WHEN","THEN","ELSE","END","CASE","IF","WITH","RECURSIVE",
        "UNION","OVER","PARTITION","CALL",
    };

    // ── 內部型別 ──────────────────────────────────────────────

    private class SqlContext
    {
        public List<string>               Tables          { get; } = new();
        public Dictionary<string, string> Aliases         { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string>            SubqueryAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int                        SubqueryCount   { get; set; }
        public string?                    CurrentDatabase { get; set; }
        public string?                    Database        { get; set; }
    }

    private class ColumnMeta
    {
        public string Name     { get; set; } = "";
        public string Type     { get; set; } = "";
        public bool   IsPK     { get; set; }
        public bool   IsFK     { get; set; }
        public bool   Nullable { get; set; }
        public string TypeLabel => Type.Length > 18 ? Type[..18] : Type;
    }

    private class ContextToken { public TokenType Type { get; set; } = TokenType.Unknown; }

    private enum TokenType
    {
        Unknown, AfterSelect, AfterFrom, AfterJoin, AfterWhere, AfterOn,
        AfterSet, AfterHaving, AfterOrderBy, AfterGroupBy,
        AfterInto, AfterUpdate, AfterUse, AfterDatabase, AfterDot,
    }
}
