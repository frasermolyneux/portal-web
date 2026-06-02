using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameServerDtoExtensions
{
    public static GameServerViewModel ToViewModel(this GameServerDto gameServerDto)
    {
        var fileTransportEnabled = gameServerDto.FileTransportEnabled;
        var transportType = gameServerDto.FileTransportType;

        var viewModel = new GameServerViewModel
        {
            GameServerId = gameServerDto.GameServerId,
            Title = gameServerDto.Title,
            GameType = gameServerDto.GameType,
            Hostname = gameServerDto.Hostname,
            QueryPort = gameServerDto.QueryPort,
            AgentEnabled = gameServerDto.AgentEnabled,
            FileTransportEnabled = fileTransportEnabled,
            FileTransportType = transportType,
            FtpEnabled = fileTransportEnabled && transportType == XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType.Ftp,
            RconEnabled = gameServerDto.RconEnabled,
            BanFileSyncEnabled = gameServerDto.BanFileSyncEnabled,
            BanFileRootPath = string.IsNullOrWhiteSpace(gameServerDto.BanFileRootPath) ? "/" : gameServerDto.BanFileRootPath,
            ServerListEnabled = gameServerDto.ServerListEnabled,
            ServerListPosition = gameServerDto.ServerListPosition
        };

        return viewModel;
    }
}