using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MySQLManager.Services;

public class CsvColumn
{
    public string CsvHeader    { get; set; } = "";
    public string? MappedColumn { get; set; }       // null = 跳過
    public string SampleValue  { get; set; } = "";
    public bool   Skip         { get; set; } = false;
}

public class CsvImportService
{
    private readonly ConnectionService _conn;
    public CsvImportService(ConnectionService conn) => _conn = conn;

    // ── 讀取 CSV 前幾列用於預覽 ──────────────────────────────

    public (List<string> headers, List<List<string>> rows) ReadPreview(
        string filePath, int previewRows = 5, char delimiter = ',', bool hasHeader = true)
    {
        var allLines = File.ReadLines(filePath, Encoding.UTF8).Take(previewRows + 2).ToList();
        if (allLines.Count == 0) return (new(), new());

        var headers = hasHeader
            ? ParseCsvLine(allLines[0], delimiter)
            : Enumerable.Range(0, ParseCsvLine(allLines[0], delimiter).Count)
                        .Select(i => $"col_{i}").ToList();

        var dataStart = hasHeader ? 1 : 0;
        var rows = allLines.Skip(dataStart).Take(previewRows)
                           .Select(l => ParseCsvLine(l, delimiter))
                           .ToList();
        return (headers, rows);
    }

    // ── 取得資料表欄位（供對映下拉使用）────────────────────────

    public async Task<List<string>> GetTableColumnsAsync(string database, string table)
    {
        var cols = await _conn.GetColumnsAsync(database, table);
        return cols.Select(c => c.Field).ToList();
    }

    // ── 執行匯入 ─────────────────────────────────────────────

    public async Task<(int imported, int failed, string? error)> ImportAsync(
        string filePath, string database, string table,
        List<CsvColumn> mapping, char delimiter, bool hasHeader, bool skipErrors,
        IProgress<(int done, int total)>? progress = null)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        var dataLines = hasHeader ? lines.Skip(1).ToArray() : lines;
        int total = dataLines.Length, imported = 0, failed = 0;

        // 取得 active mapping
        var active = mapping.Where(m => !m.Skip && m.MappedColumn != null).ToList();
        if (active.Count == 0) return (0, 0, "沒有選取任何欄位對映");

        var colNames  = string.Join(", ", active.Select(m => $"`{m.MappedColumn}`"));
        var batchSql  = new StringBuilder();
        const int batchSize = 200;

        for (int i = 0; i < dataLines.Length; i++)
        {
            progress?.Report((i + 1, total));
            if (string.IsNullOrWhiteSpace(dataLines[i])) continue;

            var fields = ParseCsvLine(dataLines[i], delimiter);

            try
            {
                var vals = active.Select(m =>
                {
                    // 找對應欄位的 CSV index
                    var idx = mapping.IndexOf(m);
                    var raw = idx < fields.Count ? fields[idx] : "";
                    if (string.IsNullOrEmpty(raw)) return "NULL";
                    raw = raw.Replace("'", "''");
                    return $"'{raw}'";
                });

                batchSql.AppendLine(
                    $"INSERT INTO `{database}`.`{table}` ({colNames}) VALUES ({string.Join(", ", vals)});");
                imported++;

                // 每 batch 執行一次
                if (imported % batchSize == 0)
                {
                    var r = await _conn.ExecuteNonQueryAsync(batchSql.ToString(), database);
                    if (!r.Success)
                    {
                        if (!skipErrors) return (imported - batchSize, failed, r.ErrorMessage);
                        failed += batchSize;
                        imported -= batchSize;
                    }
                    batchSql.Clear();
                }
            }
            catch (Exception ex)
            {
                if (!skipErrors) return (imported, failed + 1, ex.Message);
                failed++;
            }
        }

        // 剩餘批次
        if (batchSql.Length > 0)
        {
            var r = await _conn.ExecuteNonQueryAsync(batchSql.ToString(), database);
            if (!r.Success)
            {
                if (!skipErrors) return (imported, failed, r.ErrorMessage);
                failed++;
            }
        }

        return (imported, failed, null);
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        bool inQuote = false;
        var cur = new StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuote = !inQuote; }
            else if (c == delimiter && !inQuote) { result.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        result.Add(cur.ToString());
        return result;
    }
}
