// Auto-generated model classes for Lock Analysis and Event Scheduler
// This file ensures LockInfo and MySqlEvent are always available
// regardless of whether Models/DbModels.cs has been updated.
namespace MySQLManager.Models;

public class LockInfo
{
    public string WaitingTrxId   { get; set; } = "";
    public long   WaitingThread  { get; set; }
    public string WaitingQuery   { get; set; } = "";
    public string BlockingTrxId  { get; set; } = "";
    public long   BlockingThread { get; set; }
    public string BlockingQuery  { get; set; } = "";
    public int    WaitSeconds    { get; set; }
    public string LockTable      { get; set; } = "";
    public string LockType       { get; set; } = "";
    public string LockMode       { get; set; } = "";
    public string WaitLabel      => $"Thread {WaitingThread}";
    public string BlockLabel     => $"Thread {BlockingThread}";
    public string WaitQueryShort => WaitingQuery.Length > 60 ? WaitingQuery[..60] + "…" : WaitingQuery;
    public string BlockQueryShort=> BlockingQuery.Length > 60 ? BlockingQuery[..60] + "…" : BlockingQuery;
}

public class MySqlEvent
{
    public string    Name          { get; set; } = "";
    public string    EventType     { get; set; } = "RECURRING";
    public DateTime? ExecuteAt     { get; set; }
    public string    IntervalValue { get; set; } = "1";
    public string    IntervalField { get; set; } = "HOUR";
    public string    Definition    { get; set; } = "BEGIN\n  -- SQL here\nEND";
    public string    Status        { get; set; } = "ENABLED";
    public DateTime? LastExecuted  { get; set; }
    public string    OnCompletion  { get; set; } = "NOT PRESERVE";
    public DateTime? Starts        { get; set; }
    public DateTime? Ends          { get; set; }
    public string StatusIcon => Status == "ENABLED" ? "▶" : "⏸";
    public string TypeLabel  => EventType == "ONE TIME" ? "單次" : "週期";
    public string ScheduleLabel => EventType == "ONE TIME"
        ? (ExecuteAt.HasValue ? ExecuteAt.Value.ToString("MM/dd HH:mm") : "—")
        : $"每 {IntervalValue} {IntervalField}";
    public string LastExecLabel => LastExecuted.HasValue
        ? LastExecuted.Value.ToString("MM/dd HH:mm") : "從未執行";
}
