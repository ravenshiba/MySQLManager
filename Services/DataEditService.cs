using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySQLManager.Models;

namespace MySQLManager.Services;

/// <summary>
/// 處理資料列的新增 / 修改 / 刪除，自動產生對應 SQL
/// </summary>
public class DataEditService
{
    private readonly ConnectionService _conn;

    public DataEditService(ConnectionService conn) => _conn = conn;

    // ── 取得主鍵欄位清單 ─────────────────────────────────────

    public async Task<List<string>> GetPrimaryKeysAsync(string database, string table)
    {
        var result = await _conn.ExecuteQueryAsync(
            $"SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE " +
            $"WHERE TABLE_SCHEMA='{database}' AND TABLE_NAME='{table}' " +
            $"AND CONSTRAINT_NAME='PRIMARY' ORDER BY ORDINAL_POSITION;");

        var keys = new List<string>();
        if (result.Success && result.Data != null)
            foreach (DataRow row in result.Data.Rows)
                keys.Add(row[0]?.ToString() ?? "");
        return keys;
    }

    // ── UPDATE ────────────────────────────────────────────────

    public async Task<QueryResult> UpdateRowAsync(
        string database, string table,
        Dictionary<string, object?> newValues,
        Dictionary<string, object?> pkValues)
    {
        if (pkValues.Count == 0)
            return new QueryResult { Success = false, ErrorMessage = "找不到主鍵，無法更新" };

        var sets  = newValues.Keys.Select(c => $"`{c}` = {FormatValue(newValues[c])}");
        var where = pkValues.Keys.Select(c => $"`{c}` = {FormatValue(pkValues[c])}");

        var sql = $"UPDATE `{database}`.`{table}` SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", where)};";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    // ── INSERT ────────────────────────────────────────────────

    public async Task<QueryResult> InsertRowAsync(
        string database, string table,
        Dictionary<string, object?> values)
    {
        // 過濾掉 null 且非必填的欄位
        var cols = values.Where(kv => kv.Value != null && kv.Value.ToString() != "")
                         .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (cols.Count == 0)
            return new QueryResult { Success = false, ErrorMessage = "沒有任何欄位值" };

        var colNames = string.Join(", ", cols.Keys.Select(c => $"`{c}`"));
        var colVals  = string.Join(", ", cols.Values.Select(v => FormatValue(v)));

        var sql = $"INSERT INTO `{database}`.`{table}` ({colNames}) VALUES ({colVals});";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    // ── DELETE ────────────────────────────────────────────────

    public async Task<QueryResult> DeleteRowAsync(
        string database, string table,
        Dictionary<string, object?> pkValues)
    {
        if (pkValues.Count == 0)
            return new QueryResult { Success = false, ErrorMessage = "找不到主鍵，無法刪除" };

        var where = pkValues.Keys.Select(c => $"`{c}` = {FormatValue(pkValues[c])}");
        var sql = $"DELETE FROM `{database}`.`{table}` WHERE {string.Join(" AND ", where)};";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    // ── 工具 ─────────────────────────────────────────────────

    private static string FormatValue(object? val)
    {
        if (val == null || val == DBNull.Value) return "NULL";
        var s = val.ToString()!;
        // 數字型別不加引號
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
            return s;
        // 字串加引號並跳脫單引號
        return $"'{s.Replace("'", "''")}'";
    }
}
