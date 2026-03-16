using System;

namespace MySQLManager.Models;

public class SlowQueryEntry
{
    public string Schema       { get; set; } = "";
    public string DigestSql    { get; set; } = "";
    public long   ExecCount    { get; set; }
    public double AvgTimeMs    { get; set; }
    public double MaxTimeMs    { get; set; }
    public double SumTimeMs    { get; set; }
    public long   NoIndexCount { get; set; }
    public long   RowsExamined { get; set; }
    public long   RowsSent     { get; set; }

    public string SqlPreview    => DigestSql.Length > 120 ? DigestSql[..120] + "…" : DigestSql;
    public string AvgTimeLabel  => FormatMs(AvgTimeMs);
    public string MaxTimeLabel  => FormatMs(MaxTimeMs);
    public string SumTimeLabel  => FormatMs(SumTimeMs);

    public string SeverityIcon  => AvgTimeMs switch
    {
        >= 10000 => "🔴",
        >= 3000  => "🟠",
        >= 1000  => "🟡",
        _        => "🟢"
    };

    public string BarColor => AvgTimeMs switch
    {
        >= 10000 => "#EF5350",
        >= 3000  => "#FF7043",
        >= 1000  => "#FFA726",
        _        => "#1976D2"
    };

    private static string FormatMs(double ms) => ms switch
    {
        >= 60000  => $"{ms / 60000:F1}m",
        >= 1000   => $"{ms / 1000:F2}s",
        _         => $"{ms:F0}ms"
    };
}

public class TableStatEntry
{
    public string    TableName  { get; set; } = "";
    public long      RowCount   { get; set; }
    public double    DataMb     { get; set; }
    public double    IndexMb    { get; set; }
    public double    TotalMb    { get; set; }
    public string    Collation  { get; set; } = "";
    public string    Engine     { get; set; } = "";
    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
    public string    Comment    { get; set; } = "";

    public string RowCountLabel  => RowCount >= 1_000_000 ? $"{RowCount / 1_000_000.0:F1}M"
                                  : RowCount >= 1_000     ? $"{RowCount / 1_000.0:F1}K"
                                  :                         $"{RowCount:N0}";
    public string TotalMbLabel   => TotalMb >= 1024 ? $"{TotalMb/1024:F2} GB" : $"{TotalMb:F2} MB";
    public string DataMbLabel    => DataMb  >= 1024 ? $"{DataMb/1024:F2} GB"  : $"{DataMb:F2} MB";
    public string IndexMbLabel   => IndexMb >= 1024 ? $"{IndexMb/1024:F2} GB" : $"{IndexMb:F2} MB";
    public string UpdateLabel    => UpdateTime?.ToString("MM/dd HH:mm") ?? "—";
    public string CreateLabel    => CreateTime?.ToString("yyyy/MM/dd")  ?? "—";
    public string SizeBarWidth   => $"{Math.Min(TotalMb / 100 * 200, 200):F0}";
    public string SizeBarColor   => TotalMb switch
    {
        >= 500 => "#EF5350",
        >= 100 => "#FFA726",
        >= 10  => "#1976D2",
        _      => "#66BB6A"
    };
}
