using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
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
