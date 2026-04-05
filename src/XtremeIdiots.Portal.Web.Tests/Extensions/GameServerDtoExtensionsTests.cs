using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class GameServerDtoExtensionsTests
{
    private static GameServerDto CreateGameServerDto(bool botEnabled = false, bool agentEnabled = false)
    {
        // GameServerDto uses internal setters, so we serialize/deserialize to set values
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "Test Server",
            GameType = GameType.CallOfDuty4,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            ServerListPosition = 1,
            BotEnabled = botEnabled,
            AgentEnabled = agentEnabled,
            BannerServerListEnabled = true,
            PortalServerListEnabled = true,
            ChatLogEnabled = true,
            LiveTrackingEnabled = true
        });

        return Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }

    [Fact]
    public void ToViewModel_MapsAllProperties()
    {
        // Arrange
        var dto = CreateGameServerDto(botEnabled: true, agentEnabled: true);

        // Act
        var viewModel = dto.ToViewModel();

        // Assert
        Assert.Equal(dto.GameServerId, viewModel.GameServerId);
        Assert.Equal(dto.Title, viewModel.Title);
        Assert.Equal(dto.GameType, viewModel.GameType);
        Assert.Equal(dto.Hostname, viewModel.Hostname);
        Assert.Equal(dto.QueryPort, viewModel.QueryPort);
        Assert.Equal(dto.ServerListPosition, viewModel.ServerListPosition);
        Assert.Equal(dto.BannerServerListEnabled, viewModel.BannerServerListEnabled);
        Assert.Equal(dto.PortalServerListEnabled, viewModel.PortalServerListEnabled);
        Assert.Equal(dto.ChatLogEnabled, viewModel.ChatLogEnabled);
        Assert.Equal(dto.LiveTrackingEnabled, viewModel.LiveTrackingEnabled);
        Assert.Equal(dto.BotEnabled, viewModel.BotEnabled);
        Assert.Equal(dto.AgentEnabled, viewModel.AgentEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ToViewModel_MapsBotEnabledAndAgentEnabled(bool botEnabled, bool agentEnabled)
    {
        // Arrange
        var dto = CreateGameServerDto(botEnabled: botEnabled, agentEnabled: agentEnabled);

        // Act
        var viewModel = dto.ToViewModel();

        // Assert
        Assert.Equal(botEnabled, viewModel.BotEnabled);
        Assert.Equal(agentEnabled, viewModel.AgentEnabled);
    }
}
