using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using Newtonsoft.Json;

namespace MySQLManager.Services;

public enum BackupFrequency { Daily, Weekly, Hourly }

public class BackupSchedule
{
    public string   Id           { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string   Database     { get; set; } = "";
    public string   OutputDir    { get; set; } = "";
    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;
    public TimeSpan TimeOfDay    { get; set; } = TimeSpan.FromHours(2);   // 02:00
    public DayOfWeek WeekDay    { get; set; } = DayOfWeek.Sunday;
    public bool     IsEnabled    { get; set; } = true;
    public bool     IncludeDdl   { get; set; } = true;
    public bool     IncludeData  { get; set; } = true;
    public int      KeepCount    { get; set; } = 7;   // 保留最新幾份
    public DateTime? LastRun     { get; set; }
    public DateTime? NextRun     { get; set; }
    public string?  LastStatus   { get; set; }

    public string FrequencyLabel => Frequency switch
    {
        BackupFrequency.Hourly => "每小時",
        BackupFrequency.Daily  => $"每天 {TimeOfDay:hh\\:mm}",
        BackupFrequency.Weekly => $"每週{WeekDay.ToChineseDay()} {TimeOfDay:hh\\:mm}",
        _                      => ""
    };
}

public class BackupLog
{
    public DateTime Time     { get; set; } = DateTime.Now;
    public string   Database { get; set; } = "";
    public string   FilePath { get; set; } = "";
    public bool     Success  { get; set; }
    public string   Message  { get; set; } = "";
    public long     SizeBytes{ get; set; }
    public string   SizeLabel => SizeBytes > 1_048_576
        ? $"{SizeBytes / 1_048_576.0:F1} MB"
        : $"{SizeBytes / 1024.0:F0} KB";
}

public static class DayOfWeekExt
{
    public static string ToChineseDay(this DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => "一",
        DayOfWeek.Tuesday   => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday  => "四",
        DayOfWeek.Friday    => "五",
        DayOfWeek.Saturday  => "六",
        DayOfWeek.Sunday    => "日",
        _                   => ""
    };
}

public class ScheduledBackupService
{
    private readonly ConnectionService _conn;
    private readonly string _schedulePath;
    private readonly string _logPath;
    private List<BackupSchedule> _schedules = new();
    private List<BackupLog>      _logs      = new();
    private CancellationTokenSource? _cts;
    private Task? _runnerTask;

    public event Action<BackupLog>? BackupCompleted;

    public ScheduledBackupService(ConnectionService conn)
    {
        _conn = conn;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _schedulePath = Path.Combine(dir, "backup_schedules.json");
        _logPath      = Path.Combine(dir, "backup_logs.json");
        Load();
    }

    // ── 持久化 ───────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (File.Exists(_schedulePath))
                _schedules = JsonConvert.DeserializeObject<List<BackupSchedule>>(
                    File.ReadAllText(_schedulePath)) ?? new();
            if (File.Exists(_logPath))
                _logs = JsonConvert.DeserializeObject<List<BackupLog>>(
                    File.ReadAllText(_logPath)) ?? new();
        }
        catch { }
    }

    private void SaveSchedules()
    {
        try { File.WriteAllText(_schedulePath,
            JsonConvert.SerializeObject(_schedules, Formatting.Indented)); }
        catch { }
    }

    private void SaveLogs()
    {
        // 只保留最近 200 筆
        if (_logs.Count > 200) _logs = _logs.TakeLast(200).ToList();
        try { File.WriteAllText(_logPath,
            JsonConvert.SerializeObject(_logs, Formatting.Indented)); }
        catch { }
    }

    // ── CRUD ─────────────────────────────────────────────────

    public IReadOnlyList<BackupSchedule> GetSchedules() => _schedules.AsReadOnly();
    public IReadOnlyList<BackupLog>      GetLogs()      =>
        _logs.OrderByDescending(l => l.Time).ToList().AsReadOnly();

    public BackupSchedule Add(BackupSchedule s)
    {
        RecalcNextRun(s);
        _schedules.Add(s);
        SaveSchedules();
        return s;
    }

    public void Update(BackupSchedule s)
    {
        var idx = _schedules.FindIndex(x => x.Id == s.Id);
        if (idx < 0) return;
        RecalcNextRun(s);
        _schedules[idx] = s;
        SaveSchedules();
    }

    public void Delete(string id)
    {
        _schedules.RemoveAll(s => s.Id == id);
        SaveSchedules();
    }

    // ── 排程引擎 ─────────────────────────────────────────────

    public void StartScheduler()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _runnerTask = RunSchedulerAsync(_cts.Token);
    }

    public void StopScheduler() => _cts?.Cancel();

    private async Task RunSchedulerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                var now = DateTime.Now;
                foreach (var s in _schedules.Where(x => x.IsEnabled))
                {
                    if (s.NextRun.HasValue && now >= s.NextRun.Value)
                        await ExecuteBackupAsync(s);
                }
            }
            catch (TaskCanceledException) { break; }
            catch { /* 不讓排程引擎崩潰 */ }
        }
    }

    // ── 執行備份 ─────────────────────────────────────────────

    public async Task<BackupLog> ExecuteBackupAsync(BackupSchedule s)
    {
        var log = new BackupLog { Database = s.Database };
        try
        {
            Directory.CreateDirectory(s.OutputDir);
            var fileName = $"{s.Database}_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
            var filePath = Path.Combine(s.OutputDir, fileName);

            // 使用內建 mysqldump-like 方式透過 ConnectionService
            await DumpDatabaseAsync(s.Database, filePath, s.IncludeDdl, s.IncludeData);

            log.Success  = true;
            log.FilePath = filePath;
            log.SizeBytes= new FileInfo(filePath).Length;
            log.Message  = "備份成功";

            // 清理舊備份
            if (s.KeepCount > 0) PurgeOldBackups(s.OutputDir, s.Database, s.KeepCount);
        }
        catch (Exception ex)
        {
            log.Success = false;
            log.Message = ex.Message;
        }

        s.LastRun    = DateTime.Now;
        s.LastStatus = log.Success ? "✅ 成功" : $"❌ {log.Message[..Math.Min(40, log.Message.Length)]}";
        RecalcNextRun(s);
        SaveSchedules();

        _logs.Add(log);
        SaveLogs();

        BackupCompleted?.Invoke(log);
        return log;
    }

    private async Task DumpDatabaseAsync(string db, string filePath,
        bool includeDdl, bool includeData)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- MySQL Manager Auto Backup");
        sb.AppendLine($"-- Database: {db}");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- ----------------------------------------");
        sb.AppendLine($"SET FOREIGN_KEY_CHECKS=0;");
        sb.AppendLine();

        var tables = await _conn.GetTablesAsync(db);
        foreach (var tbl in tables)
        {
            if (includeDdl)
            {
                var ddl = await _conn.GetCreateTableSqlAsync(db, tbl);
                sb.AppendLine($"DROP TABLE IF EXISTS `{tbl}`;");
                sb.AppendLine(ddl + ";");
                sb.AppendLine();
            }

            if (includeData)
            {
                var qr   = await _conn.ExecuteQueryAsync($"SELECT * FROM `{db}`.`{tbl}`");
                var data = qr.Data ?? new System.Data.DataTable();
                if (data == null || data.Rows.Count == 0) continue;

                var cols = string.Join(", ", data.Columns
                    .Cast<System.Data.DataColumn>()
                    .Select(c => $"`{c.ColumnName}`"));

                foreach (System.Data.DataRow row in data.Rows)
                {
                    var vals = string.Join(", ", row.ItemArray
                        .Select(v => v == null || v == DBNull.Value
                            ? "NULL"
                            : $"'{v.ToString()!.Replace("'", "''")}'"));
                    sb.AppendLine($"INSERT INTO `{tbl}` ({cols}) VALUES ({vals});");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
        await File.WriteAllTextAsync(filePath, sb.ToString(), System.Text.Encoding.UTF8);
    }

    private static void PurgeOldBackups(string dir, string db, int keepCount)
    {
        var files = Directory.GetFiles(dir, $"{db}_*.sql")
            .OrderByDescending(f => f)
            .Skip(keepCount);
        foreach (var f in files)
            try { File.Delete(f); } catch { }
    }

    private static void RecalcNextRun(BackupSchedule s)
    {
        var now = DateTime.Now;
        s.NextRun = s.Frequency switch
        {
            BackupFrequency.Hourly =>
                now.Date.AddHours(now.Hour + 1),
            BackupFrequency.Daily =>
                now.TimeOfDay < s.TimeOfDay
                    ? now.Date + s.TimeOfDay
                    : now.Date.AddDays(1) + s.TimeOfDay,
            BackupFrequency.Weekly =>
                NextWeekday(now, s.WeekDay, s.TimeOfDay),
            _ => null
        };
    }

    private static DateTime NextWeekday(DateTime from, DayOfWeek day, TimeSpan time)
    {
        var candidate = from.Date + time;
        int diff = ((int)day - (int)candidate.DayOfWeek + 7) % 7;
        if (diff == 0 && from.TimeOfDay >= time) diff = 7;
        return candidate.AddDays(diff);
    }
}
