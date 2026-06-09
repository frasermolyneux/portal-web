using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class NamespaceSettingsSerializerTests
{
    private readonly NamespaceSettingsSerializer serializer = new();

    [Fact]
    public void AgentDisabledMarksAgentNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.Contains(AgentSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BanFilesSyncDisabledMarksBanFilesNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.BanFileSyncEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.Contains(BanFileSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void ServerListDisabledMarksServerListNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.Contains(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BuildGlobalSettingsConfigurations_IncludesBroadcastsAndServerList()
    {
        var model = new GlobalSettingsViewModel
        {
            BroadcastsEnabled = false,
            BroadcastsIntervalSeconds = 900,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = "^1Welcome to XI",
                    Enabled = true
                }
            ],
            ServerListHtmlBanner = "<b>Global banner</b>"
        };

        var configurations = serializer.BuildGlobalSettingsConfigurations(model);

        var (_, broadcastsConfiguration) = Assert.Single(configurations, configuration => configuration.Namespace == BroadcastSettingsConstants.Namespace);
        using var broadcastsDoc = System.Text.Json.JsonDocument.Parse(broadcastsConfiguration);
        Assert.False(broadcastsDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(900, broadcastsDoc.RootElement.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal("^1Welcome to XI", broadcastsDoc.RootElement.GetProperty("messages")[0].GetProperty("message").GetString());

        var (_, serverListConfiguration) = Assert.Single(configurations, configuration => configuration.Namespace == ServerListSettingsConstants.Namespace);
        using var serverListDoc = System.Text.Json.JsonDocument.Parse(serverListConfiguration);
        Assert.Equal("<b>Global banner</b>", serverListDoc.RootElement.GetProperty("htmlBanner").GetString());

        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    private static GameServerEditViewModel BuildDefaultModel()
    {
        return new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                AgentEnabled = true,
                BanFileSyncEnabled = true,
                ServerListEnabled = true
            }
        };
    }
}
