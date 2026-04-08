using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Dashboard;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the admin dashboard, aggregating data from multiple API sources.
/// </summary>
public class DashboardViewModel
{
    /// <summary>
    /// Aggregated summary counts from the repository API (servers, players, bans, reports, actions).
    /// </summary>
    public DashboardSummaryDto? Summary { get; set; }

    /// <summary>
    /// Admin activity leaderboard entries.
    /// </summary>
    public List<AdminLeaderboardEntryDto> AdminLeaderboard { get; set; } = [];

    /// <summary>
    /// Daily moderation action counts for trend visualisation.
    /// </summary>
    public List<ModerationTrendDataPointDto> ModerationTrend { get; set; } = [];

    /// <summary>
    /// Per-server utilization data (avg/peak players).
    /// </summary>
    public ServerUtilizationCollectionDto? ServerUtilization { get; set; }

    /// <summary>
    /// Agent telemetry summaries for all servers (from Application Insights, not the repository API).
    /// </summary>
    public IReadOnlyList<AgentServerSummary> AgentStatuses { get; set; } = [];

    /// <summary>
    /// True if agent telemetry data failed to load.
    /// </summary>
    public bool AgentTelemetryUnavailable { get; set; }
}
