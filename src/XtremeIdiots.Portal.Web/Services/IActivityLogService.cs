using XtremeIdiots.Portal.Web.Models.ActivityLog;

namespace XtremeIdiots.Portal.Web.Services;

public class ActivityLogQueryResult
{
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public IReadOnlyList<ActivityLogEntry> Entries { get; set; } = [];
}

public interface IActivityLogService
{
    Task<ActivityLogQueryResult> QueryEventsAsync(
        TimeSpan timeRange,
        ActivityLogCategory? category,
        string? eventName,
        bool includeReads,
        string? searchTerm,
        int skip,
        int take,
        string sortColumn,
        string sortDirection,
        CancellationToken cancellationToken = default);
}
