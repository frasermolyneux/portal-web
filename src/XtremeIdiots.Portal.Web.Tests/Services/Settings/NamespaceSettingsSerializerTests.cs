using System.Globalization;
using System.Text.Json;

using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;
using GameType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameType;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class NamespaceSettingsSerializerTests
{
    private readonly NamespaceSettingsSerializer serializer = new();

    [Fact]
    public void AgentDisabled_DoesNotMarkAgentNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(AgentSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BanFilesSyncDisabled_DoesNotMarkBanFilesNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.BanFileSyncEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(BanFileSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void ServerListDisabled_DoesNotMarkServerListNamespaceForDeletion()
    {
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
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
    public void BuildGlobalSettingsConfigurations_IncludesVpnProtectionRulesAndExclusions()
    {
        var model = new GlobalSettingsViewModel
        {
            VpnProtection = new VpnProtectionGlobalSettingsViewModel
            {
                Enabled = true,
                ExcludedPlayerTagsCsv = "Trusted VPN, SeniorAdmin",
                Rules =
                [
                    new VpnProtectionRuleViewModel
                    {
                        Id = "vpn",
                        Signal = VpnProtectionSignal.ProxyCheckIsVpn,
                        Operator = VpnProtectionComparisonOperator.Equal,
                        ExpectedValue = "true",
                        Action = VpnProtectionAction.Ban
                    }
                ]
            }
        };

        var configurations = serializer.BuildGlobalSettingsConfigurations(model);
        var (_, json) = Assert.Single(
            configurations,
            static configuration => configuration.Namespace == VpnProtectionSettingsConstants.Namespace);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("vpn", document.RootElement.GetProperty("rules")[0].GetProperty("id").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("excludedPlayerTags").GetArrayLength());
    }

    [Fact]
    public void BuildGameServerConfigurations_VpnProtectionOverride_SerializesThenDeletesWhenInherited()
    {
        var model = BuildDefaultModel();
        model.VpnProtection.Enabled = true;
        model.VpnProtection.RuleOverrides =
        [
            new VpnProtectionRuleOverrideViewModel
            {
                Id = "vpn",
                Action = VpnProtectionAction.Kick
            }
        ];

        var configurations = serializer.BuildGameServerConfigurations(
            model,
            canEditFileTransport: false,
            canEditRcon: false,
            canConfigureScreenshots: false);
        var (_, json) = Assert.Single(
            configurations,
            static configuration => configuration.Namespace == VpnProtectionSettingsConstants.Namespace);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("Kick", document.RootElement.GetProperty("ruleOverrides")[0].GetProperty("action").GetString());

        model.VpnProtection = new VpnProtectionServerSettingsViewModel();
        configurations = serializer.BuildGameServerConfigurations(
            model,
            canEditFileTransport: false,
            canEditRcon: false,
            canConfigureScreenshots: false);

        Assert.DoesNotContain(configurations, static configuration => configuration.Namespace == VpnProtectionSettingsConstants.Namespace);
        Assert.Contains(VpnProtectionSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BuildGlobalSettingsConfigurations_WelcomeMessagesDisabled_PreservesRulesAndDoesNotDeleteNamespace()
    {
        var model = new GlobalSettingsViewModel
        {
            WelcomeMessages = new WelcomeMessageGlobalSettingsViewModel
            {
                Enabled = false,
                Rules =
                [
                    new WelcomeMessageRuleEntryViewModel
                    {
                        Id = "global-rule",
                        Enabled = true,
                        Priority = 1000,
                        Visibility = WelcomeMessageVisibility.Public,
                        MessageTemplate = "Welcome {name}",
                        RequiredTagsCsv = "vip"
                    }
                ]
            }
        };

        var configurations = serializer.BuildGlobalSettingsConfigurations(model);

        var (_, welcomeConfiguration) = Assert.Single(configurations, configuration => configuration.Namespace == WelcomeMessageSettingsViewModelConstants.Namespace);
        using var welcomeDoc = JsonDocument.Parse(welcomeConfiguration);

        Assert.False(welcomeDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("global-rule", welcomeDoc.RootElement.GetProperty("rules")[0].GetProperty("id").GetString());
        Assert.Equal("Welcome {name}", welcomeDoc.RootElement.GetProperty("rules")[0].GetProperty("messageTemplate").GetString());
        Assert.DoesNotContain(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void ChatCommands_DisableThenReenable_DeletesThenRecreatesNamespaceWithSamePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.AgentEnabled = true;
        var command = model.ChatCommands.Commands.Single(x => x.Name == "fu");
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

        command.Enabled = null;
        command.OverrideMessages = false;

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);

        command.Enabled = false;
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

        model.BroadcastsEnabled = null;

        var disabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(disabledConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.Contains(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);

        model.BroadcastsEnabled = true;

        var reenabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var reenabledJson = Assert.Single(reenabledConfigurations, x => x.Namespace == BroadcastSettingsConstants.Namespace).Configuration;
        Assert.Equal(enabledJson, reenabledJson);
    }

    [Fact]
    public void ServerList_DisableThenReenable_PreservesNamespacePayload()
    {
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = true;
        model.ServerListConfigHtmlBanner = "<b>Server banner</b>";

        var enabledConfigurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var enabledJson = Assert.Single(enabledConfigurations, x => x.Namespace == ServerListSettingsConstants.Namespace).Configuration;

        model.GameServer.ServerListEnabled = false;

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);

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
        model.BroadcastsEnabled = null;  // inherit global broadcasts

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
    public void ServerList_ServerDisabled_DoesNotDeleteNamespace()
    {
        // Top-level server list toggle is a runtime flag; disabling should not
        // delete persisted namespace settings.
        var model = BuildDefaultModel();
        model.GameServer.ServerListEnabled = false;  // inherit global serverlist

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, serializer.DeletedNamespaces);
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
    public void AgentDisabled_WithServerOverrides_DoesNotDeleteAgentDependentNamespaces()
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

        _ = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        // Agent off should not trigger namespace deletion; persisted values remain
        // intact for later re-enable.
        Assert.DoesNotContain(AgentSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.DoesNotContain(WelcomeMessageSettingsViewModelConstants.Namespace, serializer.DeletedNamespaces);
        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BuildGlobalSettingsConfigurations_IncludesCod4xNamespaces()
    {
        var model = new GlobalSettingsViewModel
        {
            Cod4xPluginEnabled = true,
            Cod4xPluginVpnProtectionEnabled = true,
            Cod4xPluginRootDirectory = "/plugins",
            Cod4xPowerEnabled = true,
            Cod4xPowerDefaultPower = 44,
            Cod4xPowerTagMappingsJson = /*lang=json,strict*/ """
            [
              { "tag": "HeadAdmin", "power": 90, "enabled": true }
            ]
            """,
            Cod4xCommandsEnabled = true
        };

        var kickCommand = model.Cod4xCommands.Single(static command => string.Equals(command.Name, "kick", StringComparison.OrdinalIgnoreCase));
        kickCommand.Enabled = false;
        kickCommand.MinPower = 81;

        var configurations = serializer.BuildGlobalSettingsConfigurations(model);

        var (_, pluginJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xPluginSettingsConstants.Namespace);
        using var pluginDoc = JsonDocument.Parse(pluginJson);
        Assert.True(pluginDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(pluginDoc.RootElement.GetProperty("vpnProtectionEnabled").GetBoolean());
        Assert.Equal("/plugins", pluginDoc.RootElement.GetProperty("pluginRootDirectory").GetString());

        var (_, powerJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xPowerSettingsConstants.Namespace);
        using var powerDoc = JsonDocument.Parse(powerJson);
        Assert.True(powerDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(44, powerDoc.RootElement.GetProperty("defaultPower").GetInt32());
        Assert.Equal("HeadAdmin", powerDoc.RootElement.GetProperty("tagMappings")[0].GetProperty("tag").GetString());

        var (_, commandsJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xCommandSettingsConstants.Namespace);
        using var commandsDoc = JsonDocument.Parse(commandsJson);
        Assert.True(commandsDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(commandsDoc.RootElement.GetProperty("commands").GetProperty("kick").GetProperty("enabled").GetBoolean());
        Assert.Equal(81, commandsDoc.RootElement.GetProperty("commands").GetProperty("kick").GetProperty("minPower").GetInt32());
    }

    [Fact]
    public void BuildGameServerConfigurations_Cod4xInheritFlags_DeleteCod4xNamespaces()
    {
        var model = BuildDefaultModel();
        model.GameServer.GameType = GameType.CallOfDuty4x;
        model.Cod4xInheritPluginSettings = true;
        model.Cod4xInheritPowerSettings = true;
        model.Cod4xInheritCommandSettings = true;

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);

        Assert.DoesNotContain(configurations, static configuration => configuration.Namespace == Cod4xPluginSettingsConstants.Namespace);
        Assert.DoesNotContain(configurations, static configuration => configuration.Namespace == Cod4xPowerSettingsConstants.Namespace);
        Assert.DoesNotContain(configurations, static configuration => configuration.Namespace == Cod4xCommandSettingsConstants.Namespace);

        Assert.Contains(Cod4xPluginSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.Contains(Cod4xPowerSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.Contains(Cod4xCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BuildGameServerConfigurations_Cod4xPluginOverride_PreservesRuntimeStateAndOperationRequest()
    {
        var model = BuildDefaultModel();
        model.GameServer.GameType = GameType.CallOfDuty4x;
        model.Cod4xInheritPluginSettings = false;
        model.Cod4xPluginEnabled = true;
        model.Cod4xPluginVpnProtectionEnabled = true;
        model.Cod4xPluginRootDirectory = "/servers/cod4x/plugins";
        model.Cod4xRuntimeCurrentVersion = "1.2.3";
        model.Cod4xRuntimePreviousKnownGoodVersion = "1.2.2";
        model.Cod4xRuntimeLastOperationId = "op-123";
        model.Cod4xRuntimeLastOperationStatus = Cod4xPluginOperationStatus.Succeeded;
        model.Cod4xRuntimeLastOperationUtc = DateTimeOffset.Parse("2026-01-01T12:00:00Z", CultureInfo.InvariantCulture);
        model.Cod4xRuntimeLastError = "none";
        model.Cod4xOperationRequestOperationId = "op-124";
        model.Cod4xOperationRequestAction = Cod4xPluginOperationAction.Install;
        model.Cod4xOperationRequestTargetVersion = "1.2.4";
        model.Cod4xOperationRequestRequestedAtUtc = DateTimeOffset.Parse("2026-01-01T13:00:00Z", CultureInfo.InvariantCulture);
        model.Cod4xOperationRequestRequestedBy = "tester";

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var (_, pluginJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xPluginSettingsConstants.Namespace);

        using var pluginDoc = JsonDocument.Parse(pluginJson);
        Assert.True(pluginDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(pluginDoc.RootElement.GetProperty("vpnProtectionEnabled").GetBoolean());
        Assert.Equal("/servers/cod4x/plugins", pluginDoc.RootElement.GetProperty("pluginRootDirectory").GetString());
        Assert.Equal("1.2.3", pluginDoc.RootElement.GetProperty("runtimeState").GetProperty("currentVersion").GetString());
        Assert.Equal("Succeeded", pluginDoc.RootElement.GetProperty("runtimeState").GetProperty("lastOperationStatus").GetString());
        Assert.Equal("op-124", pluginDoc.RootElement.GetProperty("operationRequest").GetProperty("operationId").GetString());
        Assert.Equal("Install", pluginDoc.RootElement.GetProperty("operationRequest").GetProperty("action").GetString());
        Assert.Equal("1.2.4", pluginDoc.RootElement.GetProperty("operationRequest").GetProperty("targetVersion").GetString());
    }

    [Fact]
    public void BuildGameServerConfigurations_Cod4xInheritPluginWithLifecycleState_PersistsLifecycleOnlyDocument()
    {
        var model = BuildDefaultModel();
        model.GameServer.GameType = GameType.CallOfDuty4x;
        model.Cod4xInheritPluginSettings = true;
        model.Cod4xRuntimeCurrentVersion = "1.2.3";
        model.Cod4xRuntimeLastOperationId = "op-123";
        model.Cod4xRuntimeLastOperationStatus = Cod4xPluginOperationStatus.Succeeded;
        model.Cod4xOperationRequestOperationId = "op-124";
        model.Cod4xOperationRequestAction = Cod4xPluginOperationAction.Install;
        model.Cod4xOperationRequestTargetVersion = "1.2.4";
        model.Cod4xOperationRequestRequestedBy = "tester";

        var configurations = serializer.BuildGameServerConfigurations(model, canEditFileTransport: false, canEditRcon: false, canConfigureScreenshots: false);
        var (_, pluginJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xPluginSettingsConstants.Namespace);

        using var pluginDoc = JsonDocument.Parse(pluginJson);
        Assert.True(pluginDoc.RootElement.TryGetProperty("enabled", out var enabledProperty));
        Assert.Equal(JsonValueKind.Null, enabledProperty.ValueKind);
        Assert.True(pluginDoc.RootElement.TryGetProperty("vpnProtectionEnabled", out var vpnProtectionEnabledProperty));
        Assert.Equal(JsonValueKind.Null, vpnProtectionEnabledProperty.ValueKind);
        Assert.True(pluginDoc.RootElement.TryGetProperty("pluginRootDirectory", out var pluginRootDirectoryProperty));
        Assert.Equal(JsonValueKind.Null, pluginRootDirectoryProperty.ValueKind);
        Assert.Equal("1.2.3", pluginDoc.RootElement.GetProperty("runtimeState").GetProperty("currentVersion").GetString());
        Assert.Equal("op-124", pluginDoc.RootElement.GetProperty("operationRequest").GetProperty("operationId").GetString());
        Assert.DoesNotContain(Cod4xPluginSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }

    [Fact]
    public void BuildGlobalSettingsConfigurations_Cod4xPowerMappings_PreservesExistingEnabledFlag()
    {
        var model = new GlobalSettingsViewModel
        {
            Cod4xPowerEnabled = true,
            Cod4xPowerDefaultPower = 50,
            Cod4xPowerTagMappings =
            [
                new Cod4xPowerTagMappingViewModel
                {
                    Tag = "HeadAdmin",
                    Power = 90
                }
            ],
            Cod4xPowerTagMappingsJson = /*lang=json,strict*/ """
            [
              { "tag": "HeadAdmin", "power": 90, "enabled": false }
            ]
            """
        };

        var configurations = serializer.BuildGlobalSettingsConfigurations(model);
        var (_, powerJson) = Assert.Single(configurations, static configuration => configuration.Namespace == Cod4xPowerSettingsConstants.Namespace);

        using var powerDoc = JsonDocument.Parse(powerJson);
        var mapping = powerDoc.RootElement.GetProperty("tagMappings")[0];

        Assert.Equal("HeadAdmin", mapping.GetProperty("tag").GetString());
        Assert.Equal(90, mapping.GetProperty("power").GetInt32());
        Assert.False(mapping.GetProperty("enabled").GetBoolean());
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
