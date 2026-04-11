using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for displaying game server information in the public servers list
/// </summary>
/// <param name="gameServer">The game server data transfer object</param>
public class ServersGameServerViewModel(GameServerDto gameServer)
{
    /// <summary>
    /// Gets the game server data
    /// </summary>
    public GameServerDto GameServer { get; private set; } = gameServer;
}