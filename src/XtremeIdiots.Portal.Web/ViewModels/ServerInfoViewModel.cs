using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the public server info page. Contains only non-sensitive data.
/// </summary>
public class ServerInfoViewModel
{
    public required GameServerDto GameServer { get; set; }
    public GameServerLiveStatusDto? LiveStatus { get; set; }

    public List<GameServerStatDto> GameServerStats { get; set; } = [];
    public List<MapTimelineDataPoint> MapTimelineDataPoints { get; set; } = [];
}
