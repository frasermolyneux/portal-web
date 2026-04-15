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
        IReadOnlyList<ActivityLogCategory> categories,
        IReadOnlyList<string> eventNames,
        string? searchTerm,
        int skip,
        int take,
        string sortColumn,
        string sortDirection,
        CancellationToken cancellationToken = default);
}
