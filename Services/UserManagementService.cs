using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySQLManager.Models;

namespace MySQLManager.Services;

public class DbUser
{
    public string Host        { get; set; } = "%";
    public string Username    { get; set; } = "";
    public bool   IsLocked    { get; set; }
    public string FullName    => $"'{Username}'@'{Host}'";
    public string DisplayName => $"{Username} ({Host})";
}

public class DbPrivilege
{
    public string Database  { get; set; } = "*";
    public string Table     { get; set; } = "*";
    public List<string> Grants { get; set; } = new();
    public string GrantOn   => $"`{Database}`.`{Table}`".Replace("`*`", "*");
}

public class UserManagementService
{
    private readonly ConnectionService _conn;

    public static readonly string[] AllPrivileges =
    {
        "SELECT","INSERT","UPDATE","DELETE",
        "CREATE","DROP","ALTER","INDEX",
        "REFERENCES","CREATE VIEW","SHOW VIEW",
        "CREATE ROUTINE","ALTER ROUTINE","EXECUTE",
        "TRIGGER","EVENT","LOCK TABLES",
        "CREATE TEMPORARY TABLES"
    };

    public UserManagementService(ConnectionService conn) => _conn = conn;

    // ── 查詢 ──────────────────────────────────────────────────

    public async Task<List<DbUser>> GetUsersAsync()
    {
        var r = await _conn.ExecuteQueryAsync(
            "SELECT User, Host, account_locked FROM mysql.user ORDER BY User, Host");
        if (!r.Success || r.Data == null) return new();
        return r.Data.Rows.Cast<System.Data.DataRow>().Select(row => new DbUser
        {
            Username = row["User"]?.ToString() ?? "",
            Host     = row["Host"]?.ToString() ?? "%",
            IsLocked = row["account_locked"]?.ToString() == "Y"
        }).ToList();
    }

    public async Task<List<DbPrivilege>> GetUserPrivilegesAsync(string username, string host)
    {
        var r = await _conn.ExecuteQueryAsync(
            $"SHOW GRANTS FOR '{EscSql(username)}'@'{EscSql(host)}'");
        if (!r.Success || r.Data == null) return new();

        var result = new List<DbPrivilege>();
        foreach (System.Data.DataRow row in r.Data.Rows)
        {
            var grant = row[0]?.ToString() ?? "";
            // 解析 GRANT X,Y ON db.tbl TO user
            var m = System.Text.RegularExpressions.Regex.Match(grant,
                @"GRANT\s+(.+?)\s+ON\s+(.+?)\s+TO\s+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var privPart = m.Groups[1].Value.Trim();
            var onPart   = m.Groups[2].Value.Trim().Trim('`');

            var parts = onPart.Split('.');
            var db  = parts.Length > 0 ? parts[0].Trim('`') : "*";
            var tbl = parts.Length > 1 ? parts[1].Trim('`') : "*";

            var privs = privPart.Equals("ALL PRIVILEGES", StringComparison.OrdinalIgnoreCase)
                ? AllPrivileges.ToList()
                : privPart.Split(',').Select(p => p.Trim()).ToList();

            result.Add(new DbPrivilege { Database = db, Table = tbl, Grants = privs });
        }
        return result;
    }

    // ── 建立 / 修改 ───────────────────────────────────────────

    public async Task<QueryResult> CreateUserAsync(
        string username, string host, string password)
    {
        var sql = $"CREATE USER '{EscSql(username)}'@'{EscSql(host)}' " +
                  $"IDENTIFIED BY '{EscSql(password)}';";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    public async Task<QueryResult> ChangePasswordAsync(
        string username, string host, string newPassword)
    {
        var sql = $"ALTER USER '{EscSql(username)}'@'{EscSql(host)}' " +
                  $"IDENTIFIED BY '{EscSql(newPassword)}';";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    public async Task<QueryResult> DropUserAsync(string username, string host)
        => await _conn.ExecuteNonQueryAsync(
            $"DROP USER '{EscSql(username)}'@'{EscSql(host)}';");

    public async Task<QueryResult> SetLockAsync(
        string username, string host, bool locked)
    {
        var kw  = locked ? "ACCOUNT LOCK" : "ACCOUNT UNLOCK";
        var sql = $"ALTER USER '{EscSql(username)}'@'{EscSql(host)}' {kw};";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    // ── GRANT / REVOKE ────────────────────────────────────────

    public async Task<QueryResult> GrantAsync(
        string username, string host,
        IEnumerable<string> privileges, string database, string table)
    {
        var privStr = string.Join(", ", privileges);
        var on      = FormatOn(database, table);
        var sql     = $"GRANT {privStr} ON {on} TO " +
                      $"'{EscSql(username)}'@'{EscSql(host)}'; FLUSH PRIVILEGES;";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    public async Task<QueryResult> RevokeAllAsync(
        string username, string host, string database, string table)
    {
        var on  = FormatOn(database, table);
        var sql = $"REVOKE ALL PRIVILEGES ON {on} FROM " +
                  $"'{EscSql(username)}'@'{EscSql(host)}'; FLUSH PRIVILEGES;";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    public async Task<QueryResult> RevokeAsync(
        string username, string host,
        IEnumerable<string> privileges, string database, string table)
    {
        var privStr = string.Join(", ", privileges);
        var on      = FormatOn(database, table);
        var sql     = $"REVOKE {privStr} ON {on} FROM " +
                      $"'{EscSql(username)}'@'{EscSql(host)}'; FLUSH PRIVILEGES;";
        return await _conn.ExecuteNonQueryAsync(sql);
    }

    // ── 產生 SQL ──────────────────────────────────────────────

    public string GenerateGrantSql(
        string username, string host,
        IEnumerable<string> privileges, string database, string table)
    {
        var privStr = string.Join(", ", privileges);
        var on      = FormatOn(database, table);
        return $"GRANT {privStr} ON {on} TO '{username}'@'{host}';";
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string EscSql(string s) => s.Replace("'", "''");
    private static string FormatOn(string db, string tbl)
    {
        var d = db  == "*" ? "*" : $"`{db}`";
        var t = tbl == "*" ? "*" : $"`{tbl}`";
        return $"{d}.{t}";
    }
}
