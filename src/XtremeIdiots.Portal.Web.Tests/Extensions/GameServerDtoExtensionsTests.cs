using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class GameServerDtoExtensionsTests
{
    private static GameServerDto CreateGameServerDto(bool agentEnabled = false,
        bool ftpEnabled = false, bool rconEnabled = false, bool banFileSyncEnabled = false, bool serverListEnabled = false,
        int serverListPosition = 0, string banFileRootPath = "/")
    {
        // GameServerDto uses internal setters, so we serialize/deserialize to set values
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "Test Server",
            GameType = GameType.CallOfDuty4,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = agentEnabled,
            FtpEnabled = ftpEnabled,
            RconEnabled = rconEnabled,
            BanFileSyncEnabled = banFileSyncEnabled,
            BanFileRootPath = banFileRootPath,
            ServerListEnabled = serverListEnabled,
            ServerListPosition = serverListPosition
        });

        return Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }

    [Fact]
    public void ToViewModel_MapsAllProperties()
    {
        // Arrange
        var dto = CreateGameServerDto(agentEnabled: true,
            ftpEnabled: true, rconEnabled: true, banFileSyncEnabled: true, serverListEnabled: true,
            serverListPosition: 5);

        // Act
        var viewModel = dto.ToViewModel();

        // Assert
        Assert.Equal(dto.GameServerId, viewModel.GameServerId);
        Assert.Equal(dto.Title, viewModel.Title);
        Assert.Equal(dto.GameType, viewModel.GameType);
        Assert.Equal(dto.Hostname, viewModel.Hostname);
        Assert.Equal(dto.QueryPort, viewModel.QueryPort);
        Assert.Equal(dto.AgentEnabled, viewModel.AgentEnabled);
        Assert.Equal(dto.FtpEnabled, viewModel.FtpEnabled);
        Assert.Equal(dto.RconEnabled, viewModel.RconEnabled);
        Assert.Equal(dto.BanFileSyncEnabled, viewModel.BanFileSyncEnabled);
        Assert.Equal(dto.BanFileRootPath, viewModel.BanFileRootPath);
        Assert.Equal(dto.ServerListEnabled, viewModel.ServerListEnabled);
        Assert.Equal(dto.ServerListPosition, viewModel.ServerListPosition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToViewModel_MapsAgentEnabled(bool agentEnabled)
    {
        // Arrange
        var dto = CreateGameServerDto(agentEnabled: agentEnabled);

        // Act
        var viewModel = dto.ToViewModel();

        // Assert
        Assert.Equal(agentEnabled, viewModel.AgentEnabled);
    }
}
