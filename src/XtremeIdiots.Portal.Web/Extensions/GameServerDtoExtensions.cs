using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameServerDtoExtensions
{
    public static GameServerViewModel ToViewModel(this GameServerDto gameServerDto)
    {
        var viewModel = new GameServerViewModel
        {
            GameServerId = gameServerDto.GameServerId,
            Title = gameServerDto.Title,
            GameType = gameServerDto.GameType,
            Hostname = gameServerDto.Hostname,
            QueryPort = gameServerDto.QueryPort,
            AgentEnabled = gameServerDto.AgentEnabled,
            FtpEnabled = gameServerDto.FtpEnabled,
            RconEnabled = gameServerDto.RconEnabled,
            BanFileSyncEnabled = gameServerDto.BanFileSyncEnabled,
            ServerListEnabled = gameServerDto.ServerListEnabled,
            ServerListPosition = gameServerDto.ServerListPosition
        };

        return viewModel;
    }
}