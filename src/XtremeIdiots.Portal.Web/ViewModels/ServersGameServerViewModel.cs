using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for displaying game server information in the public servers list
/// </summary>
/// <param name="gameServer">The game server data transfer object</param>
/// <param name="liveStatus">The live status data for this server</param>
public class ServersGameServerViewModel(GameServerDto gameServer, GameServerLiveStatusDto? liveStatus = null)
{
    /// <summary>
    /// Gets the game server data
    /// </summary>
    public GameServerDto GameServer { get; private set; } = gameServer;

    /// <summary>
    /// Gets the live status data for the server
    /// </summary>
    public GameServerLiveStatusDto? LiveStatus { get; private set; } = liveStatus;
}