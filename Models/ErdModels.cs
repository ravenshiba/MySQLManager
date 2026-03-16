using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using MySQLManager.Services;

namespace MySQLManager.Models;

// ── ERD 資料模型 ──────────────────────────────────────────────

public class ErdColumn
{
    public string Name      { get; set; } = "";
    public string Type      { get; set; } = "";
    public bool   IsPrimary { get; set; }
    public bool   IsForeign { get; set; }
    public bool   IsNotNull { get; set; }

    public string Icon => IsPrimary ? "🔑" : IsForeign ? "🔗" : "  ";
}

public class ErdTable
{
    public string           Name    { get; set; } = "";
    public string           Database{ get; set; } = "";
    public List<ErdColumn>  Columns { get; set; } = new();

    // 畫布位置（由佈局算法決定）
    public double X { get; set; }
    public double Y { get; set; }
    public double Width  => 200;
    public double Height => HeaderHeight + Columns.Count * RowHeight;

    public const double HeaderHeight = 34;
    public const double RowHeight    = 24;
}

public class ErdRelation
{
    public string FromTable  { get; set; } = "";
    public string FromColumn { get; set; } = "";
    public string ToTable    { get; set; } = "";
    public string ToColumn   { get; set; } = "";
}

public class ErdDiagram
{
    public string             Database  { get; set; } = "";
    public List<ErdTable>     Tables    { get; set; } = new();
    public List<ErdRelation>  Relations { get; set; } = new();
}

// ── ERD 建構服務 ──────────────────────────────────────────────

public class ErdService
{
    private readonly ConnectionService _conn;

    public ErdService(ConnectionService conn) => _conn = conn;

    public async Task<ErdDiagram> BuildAsync(string database)
    {
        var diagram = new ErdDiagram { Database = database };

        // 取得所有資料表與欄位
        var tables  = await _conn.GetTablesAsync(database);
        var fkList  = await _conn.GetForeignKeysAsync(database);

        // FK 對應集合
        var fkCols = new HashSet<(string tbl, string col)>(
            fkList.Select(f => (f.Table, f.Column)));
        var pkCols = await GetPrimaryKeysAsync(database, tables);

        foreach (var tbl in tables)
        {
            var cols = await _conn.GetColumnsAsync(database, tbl);
            var erdTable = new ErdTable { Name = tbl, Database = database };

            foreach (var col in cols)
                erdTable.Columns.Add(new ErdColumn
                {
                    Name      = col.Field,
                    Type      = col.Type,
                    IsPrimary = pkCols.TryGetValue(tbl, out var pks) && pks.Contains(col.Field),
                    IsForeign = fkCols.Contains((tbl, col.Field)),
                    IsNotNull = col.Null != "YES"
                });

            diagram.Tables.Add(erdTable);
        }

        // 關聯線
        foreach (var fk in fkList)
            diagram.Relations.Add(new ErdRelation
            {
                FromTable  = fk.Table,  FromColumn = fk.Column,
                ToTable    = fk.ReferencedTable, ToColumn = fk.ReferencedColumn
            });

        // 自動佈局（力導向近似）
        LayoutTables(diagram.Tables);

        return diagram;
    }

    private async Task<Dictionary<string, HashSet<string>>> GetPrimaryKeysAsync(
        string database, List<string> tables)
    {
        var result = new Dictionary<string, HashSet<string>>();
        var sql = $@"SELECT TABLE_NAME, COLUMN_NAME
                     FROM information_schema.KEY_COLUMN_USAGE
                     WHERE TABLE_SCHEMA='{database}' AND CONSTRAINT_NAME='PRIMARY';";
        var qr = await _conn.ExecuteQueryAsync(sql);
        if (qr.Success && qr.Data != null)
            foreach (DataRow row in qr.Data.Rows)
            {
                var tbl = row[0]?.ToString() ?? "";
                var col = row[1]?.ToString() ?? "";
                if (!result.ContainsKey(tbl)) result[tbl] = new();
                result[tbl].Add(col);
            }
        return result;
    }

    // 簡單網格佈局，相關資料表盡量相鄰
    private static void LayoutTables(List<ErdTable> tables)
    {
        const double padX = 60, padY = 60;
        const double startX = 40, startY = 40;
        const double colWidth = 240, rowHeight = 260;

        // 每行最多 4 張表
        const int cols = 4;
        for (int i = 0; i < tables.Count; i++)
        {
            tables[i].X = startX + (i % cols) * (colWidth + padX);
            tables[i].Y = startY + (i / cols) * (rowHeight + padY);
        }
    }
}
