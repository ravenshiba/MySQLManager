using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MySQLManager.Services;

public class QueryHistoryEntry
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Sql         { get; set; } = string.Empty;
    public string? Database   { get; set; }
    public bool   Success     { get; set; }
    public double ExecutionMs { get; set; }
    public long   RowsAffected{ get; set; }
    public DateTime ExecutedAt{ get; set; } = DateTime.Now;

    public bool   IsFavorite   { get; set; }
    public string Tags        { get; set; } = "";
    public string DisplayTime  => ExecutedAt.ToString("MM/dd HH:mm:ss");
    public string FavStar      => IsFavorite ? "★" : "☆";
    public string StatusIcon   => Success ? "✅" : "❌";
    public string ShortSql     => Sql.Length > 80 ? Sql[..80].Replace('\n',' ') + "…" : Sql.Replace('\n',' ');
    public string ExecInfo     => Success
        ? $"{RowsAffected} 筆 | {ExecutionMs:F0} ms"
        : "失敗";
}

/// <summary>
/// 查詢歷史記錄 — 儲存於 AppData/MySQLManager/history.json，最多保留 500 筆
/// </summary>
public class QueryHistoryService
{
    private const int MaxEntries = 500;
    private readonly string _path;
    private List<QueryHistoryEntry> _entries = new();

    public IReadOnlyList<QueryHistoryEntry> Entries => _entries;

    public QueryHistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(QueryHistoryEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries)
            _entries = _entries.Take(MaxEntries).ToList();
        Save();
    }

    public List<QueryHistoryEntry> Search(string keyword) =>
        _entries.Where(e =>
            e.Sql.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            (e.Database?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
        .ToList();

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _entries = JsonSerializer.Deserialize<List<QueryHistoryEntry>>(json) ?? new();
            }
        }
        catch { _entries = new(); }
    }

    public IEnumerable<QueryHistoryEntry> GetFavorites()
        => _entries.Where(e => e.IsFavorite).OrderByDescending(e => e.ExecutedAt);

    public IEnumerable<QueryHistoryEntry> SortByDuration()
        => _entries.OrderByDescending(e => e.ExecutionMs);

    public void ToggleFavorite(Guid id)
    {
        var e = _entries.FirstOrDefault(x => x.Id == id);
        if (e != null) { e.IsFavorite = !e.IsFavorite; Save(); }
    }

    public void SetTags(Guid id, string tags)
    {
        var e = _entries.FirstOrDefault(x => x.Id == id);
        if (e != null) { e.Tags = tags; Save(); }
    }

    public void Remove(Guid id)
    {
        _entries.RemoveAll(x => x.Id == id);
        Save();
    }

    public void Clear() { _entries.Clear(); Save(); }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries,
                new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(_path, json);
        }
        catch { }
    }
}
