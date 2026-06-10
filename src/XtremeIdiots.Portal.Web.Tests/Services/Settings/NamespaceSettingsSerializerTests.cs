using System.Text.Json;

using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
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
        using var broadcastsDoc = JsonDocument.Parse(broadcastsConfiguration);
        Assert.False(broadcastsDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(900, broadcastsDoc.RootElement.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal("^1Welcome to XI", broadcastsDoc.RootElement.GetProperty("messages")[0].GetProperty("message").GetString());

        var (_, serverListConfiguration) = Assert.Single(configurations, configuration => configuration.Namespace == ServerListSettingsConstants.Namespace);
        using var serverListDoc = JsonDocument.Parse(serverListConfiguration);
        Assert.Equal("<b>Global banner</b>", serverListDoc.RootElement.GetProperty("htmlBanner").GetString());

        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void ChatCommands_DisableThenReenable_DeletesThenRecreatesNamespaceWithSamePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        var command = model.ChatCommands.Commands.Single(x => x.Name == "fu");
        command.OverrideEnabled = true;
        command.Enabled = false;
        command.OverrideMessages = true;
        command.Messages =
        [
            new BroadcastMessageViewModel
            {
                Message = "server-fu-{name}",
                Enabled = true
            }
        ];

        var enabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var enabledJson = Assert.Single(enabledConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace).Configuration;

        command.OverrideEnabled = false;
        command.OverrideMessages = false;

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);

        command.OverrideEnabled = true;
        command.OverrideMessages = true;

        var reenabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var reenabledJson = Assert.Single(reenabledConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace).Configuration;
        Assert.Equal(enabledJson, reenabledJson);
    }

    [Fact]
    public void WelcomeMessages_DisableThenReenable_DeletesThenRecreatesNamespaceWithSamePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        model.WelcomeMessages.RuleOverrides =
        [
            new WelcomeMessageRuleOverrideEntryViewModel
            {
                Id = "global-rule",
                OverrideRequiredTags = true,
                RequiredTagsCsv = "vip, trusted"
            }
        ];

        var enabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var enabledJson = Assert.Single(enabledConfigurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace).Configuration;

        model.WelcomeMessages.RuleOverrides = [];

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
        Assert.Contains(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);

        model.WelcomeMessages.RuleOverrides =
        [
            new WelcomeMessageRuleOverrideEntryViewModel
            {
                Id = "global-rule",
                OverrideRequiredTags = true,
                RequiredTagsCsv = "vip, trusted"
            }
        ];

        var reenabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var reenabledJson = Assert.Single(reenabledConfigurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace).Configuration;
        Assert.Equal(enabledJson, reenabledJson);
    }

    [Fact]
    public void Broadcasts_DisableThenReenable_DeletesThenRecreatesNamespaceWithSamePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        model.BroadcastsEnabled = true;
        model.BroadcastsIntervalSeconds = 750;
        model.BroadcastMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = "^1Welcome to XI",
                Enabled = true
            }
        ];

        var enabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var enabledJson = Assert.Single(enabledConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace).Configuration;

        model.BroadcastsEnabled = false;

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.Contains(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);

        model.BroadcastsEnabled = true;

        var reenabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var reenabledJson = Assert.Single(reenabledConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace).Configuration;
        Assert.Equal(enabledJson, reenabledJson);
    }

    [Fact]
    public void ServerList_DisableThenReenable_DeletesThenRecreatesNamespaceWithSamePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = true;
        model.ServerListConfigHtmlBanner = "<b>Server banner</b>";

        var enabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var enabledJson = Assert.Single(enabledConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace).Configuration;

        model.GameServer.ServerListEnabled = false;

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace);
        Assert.Contains(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);

        model.GameServer.ServerListEnabled = true;

        var reenabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var reenabledJson = Assert.Single(reenabledConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace).Configuration;
        Assert.Equal(enabledJson, reenabledJson);
    }

    [Fact]
    public void ChatCommands_InheritThenOverrideThenInherit_FinalStateDeletesNamespace()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        var command = model.ChatCommands.Commands.Single(x => x.Name == "fu");

        command.OverrideMessages = false;
        // Each call to BuildGameServerConfigurations() resets serializer.DeletedNamespaces,
        // allowing us to test state transitions without reinitializing the model.
        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);

        command.OverrideMessages = true;
        command.Messages =
        [
            new BroadcastMessageViewModel
            {
                Message = "override-message",
                Enabled = true
            }
        ];

        var overrideConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.Contains(overrideConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);

        command.OverrideMessages = false;
        var finalConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(finalConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void WelcomeMessages_InheritThenOverrideThenInherit_FinalStateDeletesNamespace()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;

        model.WelcomeMessages.Enabled = null;
        model.WelcomeMessages.InheritGlobalRules = true;
        model.WelcomeMessages.RuleOverrides = [];
        // Each call to BuildGameServerConfigurations() resets serializer.DeletedNamespaces,
        // allowing us to test state transitions without reinitializing the model.
        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.Contains(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);

        model.WelcomeMessages.RuleOverrides =
        [
            new WelcomeMessageRuleOverrideEntryViewModel
            {
                Id = "global-rule",
                OverrideRequiredTags = true,
                RequiredTagsCsv = "vip"
            }
        ];

        var overrideConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.Contains(overrideConfigurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
        Assert.DoesNotContain(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);

        model.WelcomeMessages.RuleOverrides = [];
        var finalConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(finalConfigurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
        Assert.Contains(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void Broadcasts_GlobalAndServerSerializers_AreParityCompatibleForEquivalentValues()
    {
        // Test override-enabled parity: server broadcasts enabled should match global broadcasts enabled
        const int interval = 600;
        const string message = "^2Parity message";

        var global = new GlobalSettingsViewModel
        {
            BroadcastsEnabled = true,
            BroadcastsIntervalSeconds = interval,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = message,
                    Enabled = true
                }
            ]
        };

        var globalConfigurations = serializer.BuildGlobalSettingsConfigurations(global);
        var globalJson = Assert.Single(globalConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace).Configuration;
        using var globalDoc = JsonDocument.Parse(globalJson);

        var server = BuildDefaultModel();
        server.GameServer.AgentEnabled = true;
        server.BroadcastsEnabled = true;
        server.BroadcastsIntervalSeconds = interval;
        server.BroadcastMessages =
        [
            new BroadcastMessageViewModel
            {
                Message = message,
                Enabled = true
            }
        ];

        var serverConfigurations = serializer.BuildGameServerConfigurations(server, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var serverJson = Assert.Single(serverConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace).Configuration;
        using var serverDoc = JsonDocument.Parse(serverJson);

        Assert.Equal(globalDoc.RootElement.GetProperty("enabled").GetBoolean(), serverDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(globalDoc.RootElement.GetProperty("intervalSeconds").GetInt32(), serverDoc.RootElement.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(globalDoc.RootElement.GetProperty("messages")[0].GetProperty("message").GetString(), serverDoc.RootElement.GetProperty("messages")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void Broadcasts_ServerInheritGlobal_DeletesNamespaceForParityWithInheritState()
    {
        // Test inherit parity: server broadcasts disabled should delete namespace (inherit global)
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        model.BroadcastsEnabled = false;  // inherit global broadcasts (tri-state not exposed, but disabled = inherit)

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(configurations, x => x.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.Contains(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void ServerList_GlobalAndServerSerializers_AreParityCompatibleForEquivalentValues()
    {
        // Test override-enabled parity: server serverlist with banner should match global serverlist with banner
        const string banner = "<b>Parity banner</b>";

        var global = new GlobalSettingsViewModel
        {
            ServerListHtmlBanner = banner
        };

        var globalConfigurations = serializer.BuildGlobalSettingsConfigurations(global);
        var globalJson = Assert.Single(globalConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace).Configuration;
        using var globalDoc = JsonDocument.Parse(globalJson);

        var server = BuildDefaultModel();
        server.GameServer.ServerListEnabled = true;
        server.ServerListConfigHtmlBanner = banner;

        var serverConfigurations = serializer.BuildGameServerConfigurations(server, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var serverJson = Assert.Single(serverConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace).Configuration;
        using var serverDoc = JsonDocument.Parse(serverJson);

        Assert.Equal(globalDoc.RootElement.GetProperty("htmlBanner").GetString(), serverDoc.RootElement.GetProperty("htmlBanner").GetString());
    }

    [Fact]
    public void ServerList_ServerDisabled_DeletesNamespaceForParityWithInheritState()
    {
        // Test inherit parity: server serverlist disabled should delete namespace (inherit global)
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = false;  // inherit global serverlist

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(configurations, x => x.Namespace == ServerListSettingsConstants.Namespace);
        Assert.Contains(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void Broadcasts_InvalidInterval_UsesDefaultIntervalInServerConfiguration()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        model.BroadcastsEnabled = true;
        model.BroadcastsIntervalSeconds = 0;

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var (_, broadcastsConfiguration) = Assert.Single(configurations, x => x.Namespace == BroadcastSettingsConstants.Namespace);
        using var doc = JsonDocument.Parse(broadcastsConfiguration);

        Assert.Equal(GameServerEditViewModel.DefaultBroadcastIntervalSeconds, doc.RootElement.GetProperty("intervalSeconds").GetInt32());
        Assert.True(doc.RootElement.GetProperty("intervalSeconds").GetInt32() > 0, "Default broadcast interval must be positive");
    }

    [Fact]
    public void AgentDisabled_WithServerOverrides_DeletesAgentDependentNamespaces()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = false;
        model.BroadcastsEnabled = true;
        model.ChatCommands.Commands.Single(x => x.Name == "fu").OverrideMessages = true;
        model.WelcomeMessages.RuleOverrides =
        [
            new WelcomeMessageRuleOverrideEntryViewModel
            {
                Id = "global-rule",
                OverrideRequiredTags = true,
                RequiredTagsCsv = "vip"
            }
        ];

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(configurations, x => x.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.DoesNotContain(configurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.DoesNotContain(configurations, x => x.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
        Assert.Contains(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.Contains(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);

        // Verify that non-agent-dependent namespaces are NOT marked for deletion
        Assert.DoesNotContain(BanFileSettingsConstants.Namespace, serializer.DeletedNamespaces);
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
