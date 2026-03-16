using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MySQLManager.Services;

public class Snippet
{
    public string Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title       { get; set; } = "";
    public string Sql         { get; set; } = "";
    public string Category    { get; set; } = "一般";
    public string Tags        { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int UseCount       { get; set; }

    public string Preview => Sql.Length > 80 ? Sql[..80].Replace('\n',' ') + "…" : Sql.Replace('\n',' ');
    public string[] TagList  => Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim()).ToArray();
}

public class SnippetService
{
    private readonly string _path;
    private List<Snippet> _snippets = new();

    public SnippetService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "snippets.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _snippets = JsonConvert.DeserializeObject<List<Snippet>>(
                    File.ReadAllText(_path)) ?? new();
        }
        catch { _snippets = new(); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonConvert.SerializeObject(_snippets, Formatting.Indented)); }
        catch { }
    }

    public IReadOnlyList<Snippet> GetAll() => _snippets.AsReadOnly();

    public IReadOnlyList<Snippet> Search(string keyword, string? category = null)
    {
        var q = _snippets.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToUpperInvariant();
            q = q.Where(s =>
                s.Title.ToUpperInvariant().Contains(kw) ||
                s.Sql.ToUpperInvariant().Contains(kw) ||
                s.Tags.ToUpperInvariant().Contains(kw));
        }
        if (!string.IsNullOrWhiteSpace(category) && category != "全部")
            q = q.Where(s => s.Category == category);
        return q.OrderByDescending(s => s.UseCount)
                .ThenByDescending(s => s.UpdatedAt)
                .ToList().AsReadOnly();
    }

    public List<string> GetCategories()
        => _snippets.Select(s => s.Category)
                    .Distinct().OrderBy(c => c)
                    .Prepend("全部").ToList();

    public Snippet Add(string title, string sql, string category = "一般", string tags = "")
    {
        var s = new Snippet { Title = title, Sql = sql, Category = category, Tags = tags };
        _snippets.Add(s);
        Save();
        return s;
    }

    public void Update(Snippet snippet)
    {
        var idx = _snippets.FindIndex(s => s.Id == snippet.Id);
        if (idx >= 0) { snippet.UpdatedAt = DateTime.Now; _snippets[idx] = snippet; Save(); }
    }

    public void Delete(string id)
    {
        _snippets.RemoveAll(s => s.Id == id);
        Save();
    }

    public void IncrementUse(string id)
    {
        var s = _snippets.FirstOrDefault(x => x.Id == id);
        if (s != null) { s.UseCount++; Save(); }
    }
}
