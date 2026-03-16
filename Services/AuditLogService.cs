using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MySQLManager.Services;

public class AuditLogEntry
{
    public DateTime  Timestamp    { get; set; } = DateTime.Now;
    public string    Connection   { get; set; } = string.Empty;
    public string    Database     { get; set; } = string.Empty;
    public string    SqlType      { get; set; } = string.Empty; // SELECT/INSERT/UPDATE/DELETE/DDL
    public string    Sql          { get; set; } = string.Empty;
    public bool      Success      { get; set; }
    public int       RowsAffected { get; set; }
    public long      ElapsedMs    { get; set; }
    public string?   ErrorMessage { get; set; }

    [JsonIgnore]
    public string TimeDisplay => Timestamp.ToString("MM/dd HH:mm:ss");
    [JsonIgnore]
    public string StatusIcon  => Success ? "✅" : "❌";
    [JsonIgnore]
    public string TypeColor   => SqlType switch
    {
        "SELECT" => "#1976D2",
        "INSERT" => "#1E8E3E",
        "UPDATE" => "#F9AB00",
        "DELETE" => "#D93025",
        "DDL"    => "#7C4DFF",
        _        => "#78909C"
    };
    [JsonIgnore]
    public string SqlPreview  => Sql.Length > 80 ? Sql[..80] + "…" : Sql;
}

public class AuditLogService
{
    private const int MaxMemoryEntries = 1000;
    private const int MaxFileEntries   = 10000;

    private readonly string _logPath;
    private readonly List<AuditLogEntry> _entries = new();

    public IReadOnlyList<AuditLogEntry> Entries => _entries;

    public AuditLogService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "audit.jsonl");
        LoadRecent();
    }

    public void Log(AuditLogEntry entry)
    {
        // 記憶體
        _entries.Insert(0, entry);
        if (_entries.Count > MaxMemoryEntries)
            _entries.RemoveAt(_entries.Count - 1);

        // 寫入檔案（JSONL 格式）
        try
        {
            File.AppendAllText(_logPath,
                JsonConvert.SerializeObject(entry) + Environment.NewLine);
        }
        catch { }
    }

    public void Log(string connection, string database, string sql,
                    bool success, int rowsAffected, long elapsedMs, string? error = null)
    {
        Log(new AuditLogEntry
        {
            Connection   = connection,
            Database     = database,
            Sql          = sql.Trim(),
            SqlType      = DetectSqlType(sql),
            Success      = success,
            RowsAffected = rowsAffected,
            ElapsedMs    = elapsedMs,
            ErrorMessage = error
        });
    }

    private void LoadRecent()
    {
        if (!File.Exists(_logPath)) return;
        try
        {
            var lines = File.ReadAllLines(_logPath)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .TakeLast(MaxMemoryEntries)
                            .Reverse();
            foreach (var line in lines)
            {
                var entry = JsonConvert.DeserializeObject<AuditLogEntry>(line);
                if (entry != null) _entries.Add(entry);
            }
        }
        catch { }
    }

    public void Clear()
    {
        _entries.Clear();
        try { File.Delete(_logPath); } catch { }
    }

    public List<AuditLogEntry> Filter(
        string? keyword = null, string? sqlType = null,
        DateTime? from = null, DateTime? to = null)
    {
        var q = _entries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(e => e.Sql.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                           || e.Connection.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(sqlType) && sqlType != "全部")
            q = q.Where(e => e.SqlType == sqlType);
        if (from.HasValue) q = q.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue)   q = q.Where(e => e.Timestamp <= to.Value);
        return q.ToList();
    }

    private static string DetectSqlType(string sql)
    {
        var trimmed = sql.TrimStart().ToUpperInvariant();
        if (trimmed.StartsWith("SELECT") || trimmed.StartsWith("SHOW") ||
            trimmed.StartsWith("DESCRIBE") || trimmed.StartsWith("EXPLAIN"))
            return "SELECT";
        if (trimmed.StartsWith("INSERT") || trimmed.StartsWith("REPLACE"))
            return "INSERT";
        if (trimmed.StartsWith("UPDATE"))
            return "UPDATE";
        if (trimmed.StartsWith("DELETE") || trimmed.StartsWith("TRUNCATE"))
            return "DELETE";
        return "DDL";
    }
}
