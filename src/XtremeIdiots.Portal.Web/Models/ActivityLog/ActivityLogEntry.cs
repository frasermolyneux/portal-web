namespace XtremeIdiots.Portal.Web.Models.ActivityLog;

public class ActivityLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? SourceComponent { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
}
