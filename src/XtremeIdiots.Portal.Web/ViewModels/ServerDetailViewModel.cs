using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
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
}
