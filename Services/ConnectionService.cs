using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using MySqlConnector;
using MySQLManager.Models;

namespace MySQLManager.Services;

public class ConnectionService : IDisposable
{
    private MySqlConnection?   _connection;
    private ConnectionProfile? _currentProfile;
    private SshTunnelService?  _sshTunnel;

    public bool IsConnected     => _connection?.State == ConnectionState.Open;
    public bool SshActive       => _sshTunnel?.IsActive == true;
    public string SshStatusText => _sshTunnel?.StatusText ?? string.Empty;
    public ConnectionProfile? CurrentProfile => _currentProfile;

    // ── 連線管理 ──────────────────────────────────────────────

    public async Task<bool> ConnectAsync(ConnectionProfile profile)
    {
        await DisconnectAsync();

        try
        {
            string host = profile.Host;
            int    port = profile.Port;

            // SSH Tunnel
            if (profile.UseSshTunnel)
            {
                _sshTunnel = new SshTunnelService();
                var localPort = await _sshTunnel.StartAsync(profile);
                host = "127.0.0.1";
                port = (int)localPort;
            }

            // 建立 MySQL 連線字串（若有 Tunnel，改用本機 port）
            var cs = profile.UseSshTunnel
                ? BuildConnectionString(profile, host, port)
                : profile.ConnectionString;

            _connection = new MySqlConnection(cs);
            await _connection.OpenAsync();
            _currentProfile = profile;
            profile.LastConnectedAt = DateTime.Now;
            return true;
        }
        catch
        {
            _sshTunnel?.Dispose();
            _sshTunnel = null;
            _connection = null;
            throw;
        }
    }

    private static string BuildConnectionString(ConnectionProfile p, string host, int port)
    {
        var db = string.IsNullOrWhiteSpace(p.DefaultDatabase) ? "" : $"Database={p.DefaultDatabase};";
        return $"Server={host};Port={port};{db}" +
               $"Uid={p.Username};Pwd={p.Password};" +
               $"SslMode={(p.UseSsl ? (p.SslVerifyServer ? "VerifyCA" : "Required") : "None")};" +
               (p.UseSsl && !string.IsNullOrEmpty(p.SslCaCert)     ? $"SslCa={p.SslCaCert};"     : "") +
               (p.UseSsl && !string.IsNullOrEmpty(p.SslClientCert) ? $"SslCert={p.SslClientCert};" : "") +
               (p.UseSsl && !string.IsNullOrEmpty(p.SslClientKey)  ? $"SslKey={p.SslClientKey};"  : "") +
               $"AllowPublicKeyRetrieval=True;";
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
        _sshTunnel?.Dispose();
        _sshTunnel = null;
        _currentProfile = null;
    }

    public async Task<bool> TestConnectionAsync(ConnectionProfile profile)
    {
        SshTunnelService? ssh = null;
        try
        {
            string host = profile.Host;
            int    port = profile.Port;
            if (profile.UseSshTunnel)
            {
                ssh = new SshTunnelService();
                var lp = await ssh.StartAsync(profile);
                host = "127.0.0.1";
                port = (int)lp;
            }
            var cs = profile.UseSshTunnel
                ? BuildConnectionString(profile, host, port)
                : profile.ConnectionString;
            await using var conn = new MySqlConnection(cs);
            await conn.OpenAsync();
            return conn.State == ConnectionState.Open;
        }
        finally { ssh?.Dispose(); }
    }

    // ── 資料庫瀏覽 ────────────────────────────────────────────

    public async Task<List<string>> GetDatabasesAsync()
    {
        var result = new List<string>();
        var dt = await ExecuteQueryAsync("SHOW DATABASES;");
        if (dt.Data != null)
            foreach (DataRow row in dt.Data.Rows)
                result.Add(row[0]?.ToString() ?? "");
        return result;
    }

    public async Task<List<string>> GetTablesAsync(string database)
    {
        var result = new List<string>();
        var dt = await ExecuteQueryAsync($"SHOW TABLES FROM `{database}`;");
        if (dt.Data != null)
            foreach (DataRow row in dt.Data.Rows)
                result.Add(row[0]?.ToString() ?? "");
        return result;
    }

    public async Task<List<string>> GetViewsAsync(string database)
    {
        var result = new List<string>();
        var sql = $"SELECT TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = '{database}';";
        var dt = await ExecuteQueryAsync(sql);
        if (dt.Data != null)
            foreach (DataRow row in dt.Data.Rows)
                result.Add(row[0]?.ToString() ?? "");
        return result;
    }

    public async Task<List<ColumnInfo>> GetColumnsAsync(string database, string table)
    {
        var result = new List<ColumnInfo>();
        var dt = await ExecuteQueryAsync($"SHOW FULL COLUMNS FROM `{database}`.`{table}`;");
        if (dt.Data != null)
            foreach (DataRow row in dt.Data.Rows)
                result.Add(new ColumnInfo
                {
                    Field   = row["Field"]?.ToString()   ?? "",
                    Type    = row["Type"]?.ToString()    ?? "",
                    Null    = row["Null"]?.ToString()    ?? "",
                    Key     = row["Key"]?.ToString()     ?? "",
                    Default = row["Default"]?.ToString(),
                    Extra   = row["Extra"]?.ToString()   ?? "",
                });
        return result;
    }

    // ── 單一結果集查詢 ────────────────────────────────────────

    public async Task<QueryResult> ExecuteQueryAsync(string sql, string? database = null)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();
        try
        {
            if (!string.IsNullOrEmpty(database))
                await _connection!.ChangeDatabaseAsync(database);

            await using var cmd    = new MySqlCommand(sql, _connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var dt = new DataTable();
            dt.Load(reader);
            sw.Stop();
            return new QueryResult
            {
                Success = true, Data = dt,
                RowsAffected = dt.Rows.Count,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds, Sql = sql
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new QueryResult
            {
                Success = false, ErrorMessage = ex.Message,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds, Sql = sql
            };
        }
    }

    // ── 多結果集查詢（執行多段 SQL，每段回傳一個 DataTable）──

    public async Task<MultiQueryResult> ExecuteMultiQueryAsync(string sql, string? database = null)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();
        var result = new MultiQueryResult { Sql = sql };
        try
        {
            if (!string.IsNullOrEmpty(database))
                await _connection!.ChangeDatabaseAsync(database);

            await using var cmd = new MySqlCommand(sql, _connection)
            {
                // 允許多語句
                CommandText = sql
            };
            // MySqlConnector 支援 AllowUserVariables + AllowLoadLocalInfile flags
            // 多結果集直接用 NextResultAsync
            await using var reader = await cmd.ExecuteReaderAsync();

            // 把 SQL 依分號切開，對應每個結果集
            var stmts = sql.Split(';')
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();

            int setIndex = 0;
            do
            {
                var stmtSql  = setIndex < stmts.Count ? stmts[setIndex] : "";
                var sqlUpper = stmtSql.TrimStart().ToUpper();
                var sqlType  = sqlUpper.StartsWith("SELECT") || sqlUpper.StartsWith("SHOW") || sqlUpper.StartsWith("DESC") ? "SELECT"
                             : sqlUpper.StartsWith("INSERT") ? "INSERT"
                             : sqlUpper.StartsWith("UPDATE") ? "UPDATE"
                             : sqlUpper.StartsWith("DELETE") ? "DELETE"
                             : "DDL";

                if (reader.HasRows || reader.FieldCount > 0)
                {
                    var dt = new DataTable { TableName = $"Result {setIndex + 1}" };
                    dt.Load(reader);
                    result.ResultSets.Add(new SingleResultSet
                    {
                        Index       = setIndex,
                        Data        = dt,
                        RowCount    = dt.Rows.Count,
                        IsSelect    = true,
                        Sql         = stmtSql,
                        SqlType     = sqlType,
                        ExecutionMs = sw.Elapsed.TotalMilliseconds
                    });
                }
                else
                {
                    result.ResultSets.Add(new SingleResultSet
                    {
                        Index        = setIndex,
                        RowsAffected = reader.RecordsAffected,
                        IsSelect     = false,
                        Sql          = stmtSql,
                        SqlType      = sqlType,
                        ExecutionMs  = sw.Elapsed.TotalMilliseconds
                    });
                }
                setIndex++;
            }
            while (await reader.NextResultAsync());

            sw.Stop();
            result.Success = true;
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
        }
        return result;
    }

    public async Task<QueryResult> ExecuteNonQueryAsync(string sql, string? database = null)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();
        try
        {
            if (!string.IsNullOrEmpty(database))
                await _connection!.ChangeDatabaseAsync(database);

            await using var cmd  = new MySqlCommand(sql, _connection);
            var rows = await cmd.ExecuteNonQueryAsync();
            sw.Stop();
            return new QueryResult
            {
                Success = true, RowsAffected = rows,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds, Sql = sql
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new QueryResult
            {
                Success = false, ErrorMessage = ex.Message,
                ExecutionTimeMs = sw.Elapsed.TotalMilliseconds, Sql = sql
            };
        }
    }

    // ── DDL 操作 ──────────────────────────────────────────────

    public async Task<QueryResult> CreateDatabaseAsync(string name,
        string charset = "utf8mb4", string collation = "utf8mb4_unicode_ci")
        => await ExecuteNonQueryAsync(
            $"CREATE DATABASE `{name}` CHARACTER SET {charset} COLLATE {collation};");

    public async Task<QueryResult> DropDatabaseAsync(string name)
        => await ExecuteNonQueryAsync($"DROP DATABASE `{name}`;");

    public async Task<QueryResult> DropTableAsync(string database, string table)
        => await ExecuteNonQueryAsync($"DROP TABLE `{database}`.`{table}`;");

    public async Task<string> GetCreateTableSqlAsync(string database, string table)
    {
        var result = await ExecuteQueryAsync($"SHOW CREATE TABLE `{database}`.`{table}`;");
        if (result.Success && result.Data?.Rows.Count > 0)
            return result.Data.Rows[0][1]?.ToString() ?? "";
        return "";
    }

    // ── Stored Procedure / Function ──────────────────────────

    public async Task<List<RoutineInfo>> GetRoutinesAsync(string database)
    {
        var sql = $@"SELECT ROUTINE_NAME, ROUTINE_TYPE, DEFINER,
                     CREATED, LAST_ALTERED, ROUTINE_COMMENT
                     FROM information_schema.ROUTINES
                     WHERE ROUTINE_SCHEMA = '{database}'
                     ORDER BY ROUTINE_TYPE, ROUTINE_NAME;";
        var result = await ExecuteQueryAsync(sql);
        var list = new List<RoutineInfo>();
        if (result.Success && result.Data != null)
            foreach (System.Data.DataRow row in result.Data.Rows)
                list.Add(new RoutineInfo
                {
                    Name        = row["ROUTINE_NAME"]?.ToString() ?? "",
                    Type        = row["ROUTINE_TYPE"]?.ToString() ?? "",
                    Definer     = row["DEFINER"]?.ToString() ?? "",
                    Comment     = row["ROUTINE_COMMENT"]?.ToString() ?? "",
                    LastAltered = row["LAST_ALTERED"]?.ToString() ?? ""
                });
        return list;
    }

    public async Task<string> GetRoutineBodyAsync(string database, string name, string type)
    {
        var col = type == "FUNCTION" ? "CREATE FUNCTION" : "CREATE PROCEDURE";
        var sql = $"SHOW CREATE {type} `{database}`.`{name}`;";
        var result = await ExecuteQueryAsync(sql);
        if (result.Success && result.Data?.Rows.Count > 0)
        {
            // SHOW CREATE PROCEDURE 回傳: Procedure, sql_mode, Create Procedure, ...
            // 找含 col 的欄
            foreach (System.Data.DataColumn dc in result.Data.Columns)
            {
                var val = result.Data.Rows[0][dc]?.ToString() ?? "";
                if (val.TrimStart().StartsWith(col, StringComparison.OrdinalIgnoreCase))
                    return val;
            }
        }
        return string.Empty;
    }

    public async Task<QueryResult> DropRoutineAsync(string database, string name, string type)
        => await ExecuteNonQueryAsync($"DROP {type} IF EXISTS `{database}`.`{name}`;");

    // ── EXPLAIN ───────────────────────────────────────────────

    public async Task<List<ExplainRow>> GetExplainAsync(string sql, string? database = null)
    {
        var explainSql = sql.TrimStart();
        if (!explainSql.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase))
            explainSql = "EXPLAIN " + explainSql;

        var result = await ExecuteQueryAsync(explainSql, database);
        var rows = new List<ExplainRow>();
        if (result.Success && result.Data != null)
            foreach (System.Data.DataRow row in result.Data.Rows)
                rows.Add(new ExplainRow
                {
                    Id         = row["id"]?.ToString() ?? "",
                    SelectType = row["select_type"]?.ToString() ?? "",
                    Table      = row["table"]?.ToString() ?? "",
                    Partitions = row["partitions"]?.ToString() ?? "",
                    Type       = row["type"]?.ToString() ?? "",
                    PossibleKeys = row["possible_keys"]?.ToString() ?? "",
                    Key        = row["key"]?.ToString() ?? "",
                    KeyLen     = row["key_len"]?.ToString() ?? "",
                    Ref        = row["ref"]?.ToString() ?? "",
                    Rows       = row["rows"]?.ToString() ?? "",
                    Filtered   = row["filtered"]?.ToString() ?? "",
                    Extra      = row["Extra"]?.ToString() ?? ""
                });
        return rows;
    }

    // ── 結構比較輔助 ─────────────────────────────────────────

    public async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(string database)
    {
        var sql = $@"SELECT TABLE_NAME, COLUMN_NAME,
            REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
          FROM information_schema.KEY_COLUMN_USAGE
          WHERE TABLE_SCHEMA = '{database}'
            AND REFERENCED_TABLE_NAME IS NOT NULL;";
        var result = await ExecuteQueryAsync(sql);
        var list = new List<ForeignKeyInfo>();
        if (result.Success && result.Data != null)
            foreach (DataRow row in result.Data.Rows)
                list.Add(new ForeignKeyInfo
                {
                    Table            = row[0]?.ToString() ?? "",
                    Column           = row[1]?.ToString() ?? "",
                    ReferencedTable  = row[2]?.ToString() ?? "",
                    ReferencedColumn = row[3]?.ToString() ?? ""
                });
        return list;
    }

    /// <summary>取得資料表欄位完整結構（含索引）供比較用</summary>
    public async Task<TableSchema> GetTableSchemaAsync(string database, string table)
    {
        var schema = new TableSchema { Database = database, TableName = table };
        schema.Columns = await GetColumnsAsync(database, table);

        // 取得索引
        var idxResult = await ExecuteQueryAsync($"SHOW INDEX FROM `{database}`.`{table}`;");
        if (idxResult.Success && idxResult.Data != null)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (DataRow row in idxResult.Data.Rows)
            {
                var keyName = row["Key_name"]?.ToString() ?? "";
                if (!seen.Add(keyName)) continue;
                schema.Indexes.Add(new IndexSchema
                {
                    KeyName    = keyName,
                    NonUnique  = Convert.ToInt32(row["Non_unique"]) == 1,
                    ColumnName = row["Column_name"]?.ToString() ?? "",
                    IndexType  = row["Index_type"]?.ToString() ?? ""
                });
            }
        }
        return schema;
    }

    // ── 監控 ─────────────────────────────────────────────────

    public async Task<ServerStatus> GetServerStatusAsync()
    {
        var status = new ServerStatus();

        // SHOW STATUS 一次取所有變數
        var r = await ExecuteQueryAsync("SHOW GLOBAL STATUS;");
        if (r.Success && r.Data != null)
        {
            var dict = new Dictionary<string, string>();
            foreach (System.Data.DataRow row in r.Data.Rows)
                dict[row[0]?.ToString() ?? ""] = row[1]?.ToString() ?? "0";

            status.Uptime          = long.TryParse(dict.GetValueOrDefault("Uptime"), out var up) ? up : 0;
            status.QueriesTotal    = long.TryParse(dict.GetValueOrDefault("Queries"), out var q) ? q : 0;
            status.SlowQueries     = long.TryParse(dict.GetValueOrDefault("Slow_queries"), out var sq) ? sq : 0;
            status.ThreadsConnected= long.TryParse(dict.GetValueOrDefault("Threads_connected"), out var tc) ? tc : 0;
            status.ThreadsRunning  = long.TryParse(dict.GetValueOrDefault("Threads_running"), out var tr) ? tr : 0;
            status.BytesSent       = long.TryParse(dict.GetValueOrDefault("Bytes_sent"), out var bs) ? bs : 0;
            status.BytesReceived   = long.TryParse(dict.GetValueOrDefault("Bytes_received"), out var br) ? br : 0;
            status.OpenTables      = long.TryParse(dict.GetValueOrDefault("Open_tables"), out var ot) ? ot : 0;
            status.SelectFullJoin  = long.TryParse(dict.GetValueOrDefault("Select_full_join"), out var sfj) ? sfj : 0;
            status.ComSelect       = long.TryParse(dict.GetValueOrDefault("Com_select"), out var cs) ? cs : 0;
            status.ComInsert       = long.TryParse(dict.GetValueOrDefault("Com_insert"), out var ci) ? ci : 0;
            status.ComUpdate       = long.TryParse(dict.GetValueOrDefault("Com_update"), out var cu) ? cu : 0;
            status.ComDelete       = long.TryParse(dict.GetValueOrDefault("Com_delete"), out var cd) ? cd : 0;
        }

        // SHOW VARIABLES
        var rv = await ExecuteQueryAsync("SHOW GLOBAL VARIABLES LIKE 'max_connections';");
        if (rv.Success && rv.Data?.Rows.Count > 0)
            status.MaxConnections = long.TryParse(rv.Data.Rows[0][1]?.ToString(), out var mc) ? mc : 151;

        // SHOW PROCESSLIST
        var rp = await ExecuteQueryAsync("SHOW FULL PROCESSLIST;");
        if (rp.Success && rp.Data != null)
            foreach (System.Data.DataRow row in rp.Data.Rows)
                status.Processes.Add(new ProcessInfo
                {
                    Id      = row["Id"]?.ToString()      ?? "",
                    User    = row["User"]?.ToString()    ?? "",
                    Host    = row["Host"]?.ToString()    ?? "",
                    Db      = row["db"]?.ToString()      ?? "",
                    Command = row["Command"]?.ToString() ?? "",
                    Time    = row["Time"]?.ToString()    ?? "0",
                    State   = row["State"]?.ToString()   ?? "",
                    Info    = row["Info"]?.ToString()    ?? ""
                });

        status.SampledAt = DateTime.Now;
        return status;
    }

    public async Task<List<MySQLManager.Models.SlowQueryEntry>> GetSlowQueriesAsync(
        double thresholdSec = 1.0, string sortBy = "avg_timer_wait", int limit = 50)
    {
        var result = new List<MySQLManager.Models.SlowQueryEntry>();
        if (!IsConnected) return result;
        try
        {
            var sortCol = sortBy switch
            {
                "總耗時"   => "sum_timer_wait",
                "執行次數" => "count_star",
                "最大耗時" => "max_timer_wait",
                _          => "avg_timer_wait"
            };
            var sql = $@"
SELECT
    IFNULL(SCHEMA_NAME, '') AS db,
    DIGEST_TEXT             AS digest_sql,
    COUNT_STAR              AS exec_count,
    ROUND(AVG_TIMER_WAIT / 1000000000.0, 2) AS avg_ms,
    ROUND(MAX_TIMER_WAIT / 1000000000.0, 2) AS max_ms,
    ROUND(SUM_TIMER_WAIT / 1000000000.0, 2) AS sum_ms,
    COUNT_NO_INDEX          AS no_index_count,
    SUM_ROWS_EXAMINED       AS rows_examined,
    SUM_ROWS_SENT           AS rows_sent
FROM performance_schema.events_statements_summary_by_digest
WHERE DIGEST_TEXT IS NOT NULL
  AND AVG_TIMER_WAIT / 1000000000.0 >= {thresholdSec.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}
ORDER BY {sortCol} DESC
LIMIT {limit}";
            await using var cmd = new MySqlCommand(sql, _connection);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new MySQLManager.Models.SlowQueryEntry
                {
                    Schema       = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    DigestSql    = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ExecCount    = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    AvgTimeMs    = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3),
                    MaxTimeMs    = reader.IsDBNull(4) ? 0 : (double)reader.GetDecimal(4),
                    SumTimeMs    = reader.IsDBNull(5) ? 0 : (double)reader.GetDecimal(5),
                    NoIndexCount = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    RowsExamined = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    RowsSent     = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                });
        }
        catch { }
        return result;
    }

    public async Task ResetStatementStatsAsync()
    {
        if (!IsConnected) return;
        try
        {
            await using var cmd = new MySqlCommand("TRUNCATE TABLE performance_schema.events_statements_summary_by_digest", _connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    public async Task<List<MySQLManager.Models.TableStatEntry>> GetTableStatsAsync(string database)
    {
        var result = new List<MySQLManager.Models.TableStatEntry>();
        if (!IsConnected) return result;
        try
        {
            var sql = $@"
SELECT
    TABLE_NAME,
    TABLE_ROWS,
    ROUND((DATA_LENGTH) / 1024 / 1024, 3)           AS data_mb,
    ROUND((INDEX_LENGTH) / 1024 / 1024, 3)          AS index_mb,
    ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024 / 1024, 3) AS total_mb,
    TABLE_COLLATION,
    ENGINE,
    CREATE_TIME,
    UPDATE_TIME,
    TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = '{database}' AND TABLE_TYPE = 'BASE TABLE'
ORDER BY (DATA_LENGTH + INDEX_LENGTH) DESC";
            await using var cmd = new MySqlCommand(sql, _connection);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new MySQLManager.Models.TableStatEntry
                {
                    TableName   = reader.GetString(0),
                    RowCount    = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    DataMb      = reader.IsDBNull(2) ? 0 : (double)reader.GetDecimal(2),
                    IndexMb     = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3),
                    TotalMb     = reader.IsDBNull(4) ? 0 : (double)reader.GetDecimal(4),
                    Collation   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Engine      = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    CreateTime  = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    UpdateTime  = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    Comment     = reader.IsDBNull(9) ? "" : reader.GetString(9),
                });
        }
        catch { }
        return result;
    }

    public async Task<List<MySQLManager.Models.TableStatEntry>> GetAllTablesStatsAsync()
    {
        var result = new List<MySQLManager.Models.TableStatEntry>();
        if (!IsConnected) return result;
        try
        {
            var sql = @"
SELECT
    CONCAT(TABLE_SCHEMA, '.', TABLE_NAME) AS full_name,
    TABLE_ROWS,
    ROUND((DATA_LENGTH) / 1024 / 1024, 3),
    ROUND((INDEX_LENGTH) / 1024 / 1024, 3),
    ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024 / 1024, 3),
    TABLE_COLLATION, ENGINE, CREATE_TIME, UPDATE_TIME, TABLE_COMMENT
FROM information_schema.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY (DATA_LENGTH + INDEX_LENGTH) DESC
LIMIT 500";
            await using var cmd = new MySqlCommand(sql, _connection);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new MySQLManager.Models.TableStatEntry
                {
                    TableName   = reader.GetString(0),
                    RowCount    = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                    DataMb      = reader.IsDBNull(2) ? 0 : (double)reader.GetDecimal(2),
                    IndexMb     = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3),
                    TotalMb     = reader.IsDBNull(4) ? 0 : (double)reader.GetDecimal(4),
                    Collation   = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Engine      = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    CreateTime  = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    UpdateTime  = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    Comment     = reader.IsDBNull(9) ? "" : reader.GetString(9),
                });
        }
        catch { }
        return result;
    }

    public async Task<QueryResult> KillProcessAsync(string processId)
        => await ExecuteNonQueryAsync($"KILL {processId};");

    // ── 備份與還原 ────────────────────────────────────────────

    /// <summary>產生單一資料庫的 SQL Dump（DDL + DML）</summary>
    public async Task BackupDatabaseAsync(string database, string filePath,
        bool includeDdl, bool includeData, IProgress<string>? progress = null)
    {
        var tables = await GetTablesAsync(database);
        await using var writer = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);

        await writer.WriteLineAsync($"-- MySQL Manager Backup");
        await writer.WriteLineAsync($"-- Database: {database}");
        await writer.WriteLineAsync($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"-- Tables: {tables.Count}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"SET FOREIGN_KEY_CHECKS=0;");
        await writer.WriteLineAsync($"SET SQL_MODE='NO_AUTO_VALUE_ON_ZERO';");
        await writer.WriteLineAsync();

        foreach (var table in tables)
        {
            progress?.Report($"備份 {table}…");

            if (includeDdl)
            {
                await writer.WriteLineAsync($"-- Structure for `{table}`");
                await writer.WriteLineAsync($"DROP TABLE IF EXISTS `{table}`;");
                var ddl = await GetCreateTableSqlAsync(database, table);
                await writer.WriteLineAsync(ddl + ";");
                await writer.WriteLineAsync();
            }

            if (includeData)
            {
                progress?.Report($"備份資料 {table}…");
                var result = await ExecuteQueryAsync(
                    $"SELECT * FROM `{database}`.`{table}`;");
                if (result.Success && result.Data != null && result.Data.Rows.Count > 0)
                {
                    await writer.WriteLineAsync($"-- Data for `{table}`");
                    foreach (System.Data.DataRow row in result.Data.Rows)
                    {
                        var cols = string.Join(", ",
                            result.Data.Columns.Cast<System.Data.DataColumn>()
                                .Select(c => $"`{c.ColumnName}`"));
                        var vals = string.Join(", ",
                            result.Data.Columns.Cast<System.Data.DataColumn>()
                                .Select(c => row[c] == DBNull.Value
                                    ? "NULL"
                                    : $"'{row[c]?.ToString()?.Replace("'", "''")}'"));
                        await writer.WriteLineAsync(
                            $"INSERT INTO `{table}` ({cols}) VALUES ({vals});");
                    }
                    await writer.WriteLineAsync();
                }
            }
        }

        await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS=1;");
        progress?.Report("備份完成");
    }

    /// <summary>執行 SQL 還原檔（逐語句執行）</summary>
    public async Task<(int ok, int fail, List<string> errors)> RestoreAsync(
        string filePath, string? database,
        IProgress<(int done, int total)>? progress = null)
    {
        var sql = await System.IO.File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);

        // 簡易分割語句
        var stmts = sql.Split(';')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("--"))
            .ToList();

        int ok = 0, fail = 0;
        var errors = new List<string>();

        for (int i = 0; i < stmts.Count; i++)
        {
            progress?.Report((i + 1, stmts.Count));
            var r = await ExecuteNonQueryAsync(stmts[i], database);
            if (r.Success) ok++;
            else { fail++; errors.Add($"[{i+1}] {r.ErrorMessage}"); }
        }
        return (ok, fail, errors);
    }

    // ── 工具 ─────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("尚未連線到 MySQL 伺服器");
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _sshTunnel?.Dispose();
    }

    // ══════════════════════════════════════════════════════════════
    // Lock Analysis
    // ══════════════════════════════════════════════════════════════
    public async Task<List<LockInfo>> GetLocksAsync()
    {
        var sql = @"
SELECT
    r.trx_id                AS WaitingTrxId,
    r.trx_mysql_thread_id   AS WaitingThread,
    r.trx_query             AS WaitingQuery,
    b.trx_id                AS BlockingTrxId,
    b.trx_mysql_thread_id   AS BlockingThread,
    b.trx_query             AS BlockingQuery,
    TIMESTAMPDIFF(SECOND, r.trx_wait_started, NOW()) AS WaitSeconds,
    l.lock_table            AS LockTable,
    l.lock_type             AS LockType,
    l.lock_mode             AS LockMode
FROM information_schema.INNODB_LOCK_WAITS  w
JOIN information_schema.INNODB_TRX         r ON r.trx_id = w.requesting_trx_id
JOIN information_schema.INNODB_TRX         b ON b.trx_id = w.blocking_trx_id
JOIN information_schema.INNODB_LOCKS       l ON l.lock_id = w.requested_lock_id";
        var result = await ExecuteQueryAsync(sql);
        var list   = new List<LockInfo>();
        if (result.Data == null) return list;
        foreach (System.Data.DataRow row in result.Data.Rows)
        {
            list.Add(new LockInfo
            {
                WaitingTrxId  = row["WaitingTrxId"]?.ToString()  ?? "",
                WaitingThread = Convert.ToInt64(row["WaitingThread"]),
                WaitingQuery  = row["WaitingQuery"]?.ToString()   ?? "",
                BlockingTrxId = row["BlockingTrxId"]?.ToString()  ?? "",
                BlockingThread= Convert.ToInt64(row["BlockingThread"]),
                BlockingQuery = row["BlockingQuery"]?.ToString()   ?? "",
                WaitSeconds   = row["WaitSeconds"] == DBNull.Value ? 0 : Convert.ToInt32(row["WaitSeconds"]),
                LockTable     = row["LockTable"]?.ToString()       ?? "",
                LockType      = row["LockType"]?.ToString()        ?? "",
                LockMode      = row["LockMode"]?.ToString()        ?? "",
            });
        }
        return list;
    }

    public async Task<List<LockInfo>> GetLocksV8Async()
    {
        // MySQL 8.0+ uses performance_schema instead of INNODB_LOCK_WAITS
        var sql = @"
SELECT
    r.THREAD_ID             AS WaitingThread,
    r.SQL_TEXT              AS WaitingQuery,
    b.THREAD_ID             AS BlockingThread,
    b.SQL_TEXT              AS BlockingQuery,
    w.OBJECT_SCHEMA         AS LockSchema,
    w.OBJECT_NAME           AS LockTable,
    w.LOCK_TYPE             AS LockType,
    w.LOCK_MODE             AS LockMode,
    TIMESTAMPDIFF(SECOND, w.TIMER_WAIT/1000000000000, NOW()) AS WaitSeconds
FROM performance_schema.data_lock_waits  dw
JOIN performance_schema.data_locks       w  ON w.ENGINE_LOCK_ID = dw.REQUESTING_ENGINE_LOCK_ID
JOIN performance_schema.threads          r  ON r.THREAD_ID = dw.REQUESTING_THREAD_ID
JOIN performance_schema.threads          b  ON b.THREAD_ID = dw.BLOCKING_THREAD_ID
LIMIT 100";
        var result = await ExecuteQueryAsync(sql);
        var list   = new List<LockInfo>();
        if (result.Data == null) return list;
        foreach (System.Data.DataRow row in result.Data.Rows)
        {
            list.Add(new LockInfo
            {
                WaitingTrxId   = row["WaitingThread"]?.ToString() ?? "",
                WaitingThread  = row["WaitingThread"] == DBNull.Value ? 0 : Convert.ToInt64(row["WaitingThread"]),
                WaitingQuery   = row["WaitingQuery"]?.ToString()  ?? "",
                BlockingTrxId  = row["BlockingThread"]?.ToString() ?? "",
                BlockingThread = row["BlockingThread"] == DBNull.Value ? 0 : Convert.ToInt64(row["BlockingThread"]),
                BlockingQuery  = row["BlockingQuery"]?.ToString() ?? "",
                WaitSeconds    = row["WaitSeconds"] == DBNull.Value ? 0 : Convert.ToInt32(row["WaitSeconds"]),
                LockTable      = (row["LockSchema"]?.ToString() ?? "") + "." + (row["LockTable"]?.ToString() ?? ""),
                LockType       = row["LockType"]?.ToString() ?? "",
                LockMode       = row["LockMode"]?.ToString() ?? "",
            });
        }
        return list;
    }

    // ══════════════════════════════════════════════════════════════
    // MySQL Event Scheduler
    // ══════════════════════════════════════════════════════════════
    public async Task<List<MySqlEvent>> GetEventsAsync(string database)
    {
        var sql = $@"SELECT
    EVENT_NAME, EVENT_TYPE, EXECUTE_AT, INTERVAL_VALUE, INTERVAL_FIELD,
    EVENT_DEFINITION, STATUS, LAST_EXECUTED, STARTS, ENDS, ON_COMPLETION
FROM information_schema.EVENTS
WHERE EVENT_SCHEMA = '{database}'
ORDER BY EVENT_NAME";
        var result = await ExecuteQueryAsync(sql);
        var list   = new List<MySqlEvent>();
        if (result.Data == null) return list;
        foreach (System.Data.DataRow row in result.Data.Rows)
        {
            list.Add(new MySqlEvent
            {
                Name           = row["EVENT_NAME"]?.ToString()       ?? "",
                EventType      = row["EVENT_TYPE"]?.ToString()       ?? "",
                ExecuteAt      = row["EXECUTE_AT"]  == DBNull.Value ? null : Convert.ToDateTime(row["EXECUTE_AT"]),
                IntervalValue  = row["INTERVAL_VALUE"]?.ToString()   ?? "",
                IntervalField  = row["INTERVAL_FIELD"]?.ToString()   ?? "",
                Definition     = row["EVENT_DEFINITION"]?.ToString() ?? "",
                Status         = row["STATUS"]?.ToString()           ?? "",
                LastExecuted   = row["LAST_EXECUTED"] == DBNull.Value ? null : Convert.ToDateTime(row["LAST_EXECUTED"]),
                OnCompletion   = row["ON_COMPLETION"]?.ToString()    ?? "",
            });
        }
        return list;
    }

    public async Task<QueryResult> CreateOrReplaceEventAsync(string database, MySqlEvent ev)
    {
        var body = ev.EventType == "ONE TIME"
            ? $"ON SCHEDULE AT '{ev.ExecuteAt:yyyy-MM-dd HH:mm:ss}'"
            : $"ON SCHEDULE EVERY {ev.IntervalValue} {ev.IntervalField}" +
              (ev.Starts.HasValue ? $" STARTS '{ev.Starts:yyyy-MM-dd HH:mm:ss}'" : "") +
              (ev.Ends.HasValue   ? $" ENDS '{ev.Ends:yyyy-MM-dd HH:mm:ss}'"     : "");
        var completion = ev.OnCompletion == "PRESERVE" ? "ON COMPLETION PRESERVE" : "ON COMPLETION NOT PRESERVE";
        var status = ev.Status == "DISABLED" ? "DISABLE" : "ENABLE";
        var sql = $@"USE `{database}`;
DROP EVENT IF EXISTS `{ev.Name}`;
CREATE EVENT `{ev.Name}`
{body}
{completion}
{status}
DO
{ev.Definition};";
        return await ExecuteQueryAsync(sql);
    }

    public async Task<QueryResult> DropEventAsync(string database, string eventName)
        => await ExecuteQueryAsync($"DROP EVENT IF EXISTS `{database}`.`{eventName}`");

    public async Task<QueryResult> SetEventStatusAsync(string database, string eventName, bool enable)
        => await ExecuteQueryAsync($"ALTER EVENT `{database}`.`{eventName}` {(enable ? "ENABLE" : "DISABLE")}");

}

// ── 模型 ─────────────────────────────────────────────────────

public class ForeignKeyInfo
{
    public string Table            { get; set; } = "";
    public string Column           { get; set; } = "";
    public string ReferencedTable  { get; set; } = "";
    public string ReferencedColumn { get; set; } = "";
}

public class MultiQueryResult
{
    public bool   Success         { get; set; }
    public string? ErrorMessage   { get; set; }
    public double ExecutionTimeMs { get; set; }
    public string? Sql            { get; set; }
    public List<SingleResultSet> ResultSets { get; set; } = new();
}

public class SingleResultSet
{
    public int        Index        { get; set; }
    public bool       IsSelect     { get; set; }
    public DataTable? Data         { get; set; }
    public int        RowCount     { get; set; }
    public int        RowsAffected { get; set; }
    public double     ExecutionMs  { get; set; }
    public string?    Sql          { get; set; }
    public string     SqlType      { get; set; } = "";   // SELECT / INSERT / UPDATE / DELETE / DDL
    public string     TabLabel     => IsSelect
        ? $"結果 {Index + 1}  ({RowCount} 筆)"
        : $"結果 {Index + 1}  ({RowsAffected} 筆↑)";
    public string     TabIcon      => SqlType switch {
        "SELECT" => "🔍",
        "INSERT" => "➕",
        "UPDATE" => "✏️",
        "DELETE" => "🗑",
        _        => "⚡"
    };
    public string     StatusText   => IsSelect
        ? $"{RowCount:N0} 筆  •  {ExecutionMs:F1} ms"
        : $"影響 {RowsAffected:N0} 筆  •  {ExecutionMs:F1} ms";
    public bool       IsActive     { get; set; }
}

public class TableSchema
{
    public string Database  { get; set; } = "";
    public string TableName { get; set; } = "";
    public List<MySQLManager.Models.ColumnInfo> Columns { get; set; } = new();
    public List<IndexSchema> Indexes { get; set; } = new();
}

public class IndexSchema
{
    public string KeyName    { get; set; } = "";
    public bool   NonUnique  { get; set; }
    public string ColumnName { get; set; } = "";
    public string IndexType  { get; set; } = "";
}

public class RoutineInfo
{
    public string Name        { get; set; } = "";
    public string Type        { get; set; } = "";   // PROCEDURE / FUNCTION
    public string Definer     { get; set; } = "";
    public string Comment     { get; set; } = "";
    public string LastAltered { get; set; } = "";
    public string Icon        => Type == "FUNCTION" ? "⚡" : "⚙️";
}

public class ExplainRow
{
    public string Id           { get; set; } = "";
    public string SelectType   { get; set; } = "";
    public string Table        { get; set; } = "";
    public string Partitions   { get; set; } = "";
    public string Type         { get; set; } = "";
    public string PossibleKeys { get; set; } = "";
    public string Key          { get; set; } = "";
    public string KeyLen       { get; set; } = "";
    public string Ref          { get; set; } = "";
    public string Rows         { get; set; } = "";
    public string Filtered     { get; set; } = "";
    public string Extra        { get; set; } = "";

    // 效能評估：type 越好分數越高
    public int PerformanceScore => Type switch
    {
        "system" or "const"  => 100,
        "eq_ref"             => 90,
        "ref"                => 75,
        "range"              => 60,
        "index"              => 30,
        "ALL"                => 5,
        _                    => 50
    };
    public string PerformanceColor => PerformanceScore switch
    {
        >= 90 => "#66BB6A",
        >= 60 => "#FFA726",
        >= 30 => "#FF7043",
        _     => "#EF5350"
    };
    public string PerformanceLabel => PerformanceScore switch
    {
        >= 90 => "優",
        >= 60 => "良",
        >= 30 => "差",
        _     => "慢"
    };
}

public class ServerStatus
{
    public DateTime SampledAt       { get; set; }
    public long Uptime              { get; set; }
    public long QueriesTotal        { get; set; }
    public long SlowQueries         { get; set; }
    public long ThreadsConnected    { get; set; }
    public long ThreadsRunning      { get; set; }
    public long MaxConnections      { get; set; } = 151;
    public long BytesSent           { get; set; }
    public long BytesReceived       { get; set; }
    public long OpenTables          { get; set; }
    public long SelectFullJoin      { get; set; }
    public long InnodbPagesRead     { get; set; }
    public long InnodbPagesWritten  { get; set; }
    public long ComSelect           { get; set; }
    public long ComInsert           { get; set; }
    public long ComUpdate           { get; set; }
    public long ComDelete           { get; set; }
    public List<ProcessInfo> Processes { get; set; } = new();

    public string UptimeLabel =>
        TimeSpan.FromSeconds(Uptime) is var ts
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : "—";
    public string BytesSentLabel     => FormatBytes(BytesSent);
    public string BytesReceivedLabel => FormatBytes(BytesReceived);
    private static string FormatBytes(long b) => b switch
    {
        >= 1_073_741_824 => $"{b / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{b / 1_048_576.0:F1} MB",
        >= 1_024         => $"{b / 1_024.0:F1} KB",
        _                => $"{b} B"
    };
}

public class ProcessInfo
{
    public string Id      { get; set; } = "";
    public string User    { get; set; } = "";
    public string Host    { get; set; } = "";
    public string Db      { get; set; } = "";
    public string Command { get; set; } = "";
    public string Time    { get; set; } = "0";
    public string State   { get; set; } = "";
    public string Info    { get; set; } = "";
    public string TimeLabel => int.TryParse(Time, out var t) && t > 0 ? $"{t}s" : "";
    public string RowColor  => int.TryParse(Time, out var t) ? t switch
    {
        >= 30 => "#7F1A1A",
        >= 5  => "#3A2A10",
        _     => "Transparent"
    } : "Transparent";
}
