using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MySQLManager.Services;

public class ColumnWidthService
{
    private readonly string _path;
    // Key: "database.table" → (columnName → width)
    private Dictionary<string, Dictionary<string, double>> _data = new();

    public ColumnWidthService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "column_widths.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _data = JsonConvert.DeserializeObject<
                    Dictionary<string, Dictionary<string, double>>>(
                    File.ReadAllText(_path)) ?? new();
        }
        catch { _data = new(); }
    }

    public void Save(string tableKey, string column, double width)
    {
        if (!_data.ContainsKey(tableKey))
            _data[tableKey] = new();
        _data[tableKey][column] = Math.Round(width, 1);
        Persist();
    }

    public void SaveAll(string tableKey, IEnumerable<(string Col, double Width)> widths)
    {
        _data[tableKey] = new();
        foreach (var (col, w) in widths)
            _data[tableKey][col] = Math.Round(w, 1);
        Persist();
    }

    public Dictionary<string, double>? GetWidths(string tableKey)
        => _data.TryGetValue(tableKey, out var d) ? d : null;

    private void Persist()
    {
        try { File.WriteAllText(_path, JsonConvert.SerializeObject(_data, Formatting.None)); }
        catch { }
    }
}
