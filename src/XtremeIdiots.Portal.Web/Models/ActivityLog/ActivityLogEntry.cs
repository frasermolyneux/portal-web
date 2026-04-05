namespace XtremeIdiots.Portal.Web.Models.ActivityLog;

public class ActivityLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
}
