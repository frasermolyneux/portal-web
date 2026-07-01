using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the unified admin server detail page with tabbed sections.
/// </summary>
public class ServerDetailViewModel
{
    public required GameServerDto GameServer { get; set; }
    public GameServerLiveStatusDto? LiveStatus { get; set; }

    // Tab permission flags — determines which tabs are rendered
    public bool CanViewRcon { get; set; }
    public bool CanViewChatLog { get; set; }
    public bool CanViewMapRotation { get; set; }
    public bool CanViewStatus { get; set; }
    public bool CanEditServer { get; set; }
    public bool CanViewFeedEvents { get; set; }
    public bool CanManageCoD4xPluginLifecycle { get; set; }

    // Fine-grained RCON action flags — determines which buttons are rendered within the RCON tab
    public bool CanSay { get; set; }
    public bool CanChangeMap { get; set; }
    public bool CanRestartServer { get; set; }

    // Overview tab data
    public AgentServerStatus? AgentStatus { get; set; }
    public List<GameServerStatDto> GameServerStats { get; set; } = [];
    public List<MapTimelineDataPoint> MapTimelineDataPoints { get; set; } = [];

    // Agent & Ban File tab data
    public List<BanFileMonitorDto> BanFileMonitors { get; set; } = [];

    // CoD4x plugin lifecycle state
    public bool? Cod4xPluginEnabled { get; set; }
    public string? Cod4xPluginRootDirectory { get; set; }
    public string? Cod4xRuntimeCurrentVersion { get; set; }
    public string? Cod4xRuntimePreviousKnownGoodVersion { get; set; }
    public string? Cod4xRuntimeLastOperationId { get; set; }
    public Cod4xPluginOperationStatus Cod4xRuntimeLastOperationStatus { get; set; } = Cod4xPluginOperationStatus.Unknown;
    public DateTimeOffset? Cod4xRuntimeLastOperationUtc { get; set; }
    public string? Cod4xRuntimeLastError { get; set; }
    public string? Cod4xOperationRequestOperationId { get; set; }
    public Cod4xPluginOperationAction Cod4xOperationRequestAction { get; set; } = Cod4xPluginOperationAction.Unknown;
    public string? Cod4xOperationRequestTargetVersion { get; set; }
    public DateTimeOffset? Cod4xOperationRequestRequestedAtUtc { get; set; }
    public string? Cod4xOperationRequestRequestedBy { get; set; }
    public List<string> Cod4xAvailableVersions { get; set; } = [];
}
