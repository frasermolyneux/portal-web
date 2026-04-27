using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.CentralBanFileStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model for the ban file monitor dashboard. The view consumes
/// status snapshots written by portal-server-agent (per server) and portal-sync
/// (per game type) and renders them as a single "is the fleet protected?" view.
/// </summary>
public sealed class BanFileMonitorsDashboardViewModel
{
    public required IReadOnlyList<BanFileMonitorDto> Monitors { get; init; }

    public required IReadOnlyDictionary<Guid, GameServerLiveStatusDto> LiveStatusLookup { get; init; }

    /// <summary>
    /// Per-server config namespace lookup (used for FTP host display in the table).
    /// Same shape as other admin views in this project.
    /// </summary>
    public required IReadOnlyDictionary<Guid, Dictionary<string, System.Text.Json.JsonElement>> ServerConfigs { get; init; }

    public required IReadOnlyList<BanFileMonitorGameTypeCard> GameTypeCards { get; init; }
}

/// <summary>
/// Per-game-type roll-up card. Renders three signals — active ban totals (DB),
/// central blob freshness + counts (portal-sync), and the implied DB↔central
/// drift (DB total vs central <c>BanSyncLineCount</c>).
/// </summary>
public sealed class BanFileMonitorGameTypeCard
{
    public required GameType GameType { get; init; }
    public ActiveBanCountsDto? ActiveBanCounts { get; init; }
    public CentralBanFileStatusDto? CentralStatus { get; init; }
}
