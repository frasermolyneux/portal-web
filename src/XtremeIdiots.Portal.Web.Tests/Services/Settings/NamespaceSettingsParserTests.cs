using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.FileTransport;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Shared;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class NamespaceSettingsParserTests
{
    private readonly NamespaceSettingsParser parser = new();
    private readonly ILogger logger = Mock.Of<ILogger>();

    [Fact]
    public void BroadcastMapping_LegacyBooleanStrings_DoNotConvertMessageText()
    {
        const string json = /*lang=json,strict*/ """{"enabled":"false","intervalSeconds":500,"messages":[{"message":"true","enabled":"true"}]}""";
        var config = BuildConfiguration(BroadcastSettingsConstants.Namespace, json);
        var global = new GlobalSettingsViewModel();
        var server = new GameServerEditViewModel();
        parser.PopulateGlobalSettingsViewModel(global, config, logger);
        parser.PopulateGameServerSettingsViewModel(server, config, logger);
        parser.PopulateGameServerGlobalDefaults(server, config, logger);

        Assert.False(BroadcastEditorCompatibility.RequiresReadOnly(json));
        Assert.False(global.BroadcastsEnabled);
        Assert.Equal("true", Assert.Single(global.BroadcastMessages).Message);
        Assert.False(server.BroadcastsEnabled);
        Assert.Equal("true", Assert.Single(server.BroadcastMessages).Message);
        Assert.False(server.GlobalBroadcastsEnabled);
        Assert.Equal("true", Assert.Single(server.GlobalBroadcastMessages).Message);
    }

    [Fact]
    public void ChatCommandsMapping_LegacyBooleanStrings_DoNotConvertMessageText()
    {
        const string json = /*lang=json,strict*/ """{"defaults":{"enabled":"false"},"commands":{"fu":{"enabled":"true","settings":{"messages":[{"message":"true","enabled":"false"}]}}}}""";
        var config = BuildConfiguration(ChatCommandSettingsConstants.Namespace, json);
        var global = new GlobalSettingsViewModel();
        var server = new GameServerEditViewModel();
        parser.PopulateGlobalSettingsViewModel(global, config, logger);
        parser.PopulateGameServerSettingsViewModel(server, config, logger);
        parser.PopulateGameServerGlobalDefaults(server, config, logger);

        Assert.False(ChatCommandEditorCompatibility.RequiresReadOnly(json));
        Assert.False(global.ChatCommands.DefaultsEnabled);
        var globalFu = global.ChatCommands.Commands.Single(command => command.Name == "fu");
        Assert.True(globalFu.Enabled);
        Assert.Equal("true", Assert.Single(globalFu.Messages).Message);
        Assert.False(globalFu.Messages[0].Enabled);
        var serverFu = server.ChatCommands.Commands.Single(command => command.Name == "fu");
        Assert.True(serverFu.Enabled);
        Assert.Equal("true", Assert.Single(serverFu.Messages).Message);
        Assert.False(server.GlobalChatCommands.DefaultsEnabled);
        Assert.Equal("true", Assert.Single(server.GlobalChatCommands.Commands.Single(command => command.Name == "fu").Messages).Message);
    }

    [Fact]
    public void TryParseVpnProtectionSettings_UniversalDocument_ReturnsTypedSettings()
    {
        var config = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [{
                "id": "vpn",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": {
                    "signal": "ProxyCheckIsVpn",
                    "operator": "Equal",
                    "expectedValue": "true"
                  }
                },
                "actions": [{
                  "id": "observe",
                  "typeId": "moderation.observation",
                  "parameters": {
                    "reason": "VPN Protection {ruleId}",
                    "postToForums": false
                  },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }]
            }
            """);

        Assert.True(parser.TryParseVpnProtectionSettings(config, out var settings, out var errors), string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal("vpn-protection", settings!.FeatureId);
        Assert.True(settings.Enabled);
        var rule = Assert.Single(settings.Rules);
        var condition = rule.Condition!;
        Assert.Equal("vpn", rule.Id);
        Assert.Equal("vpn.intelligence", condition.ConditionId);
        Assert.Equal("ProxyCheckIsVpn", condition.Parameters!["signal"].ToString());
        Assert.Equal("moderation.observation", Assert.Single(rule.Actions).TypeId);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_PreservesRuleEnvelopeExtensionData()
    {
        var vpnConfiguration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [],
              "providerMetadata": { "source": "saved-global", "revision": 7 }
            }
            """);
        var siegeConfiguration = BuildConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "siege-mode",
              "enabled": true,
              "durationMinutes": 240,
              "rules": [],
              "futureSiegePolicy": { "source": "saved-global", "mode": "strict" }
            }
            """);
        var model = new GlobalSettingsViewModel();

        parser.PopulateGlobalSettingsViewModel(model, vpnConfiguration, logger);
        parser.PopulateGlobalSettingsViewModel(model, siegeConfiguration, logger);

        Assert.Equal("saved-global", model.VpnProtection.EnvelopeExtensionData["providerMetadata"]
            .GetProperty("source").GetString());
        Assert.Equal(7, model.VpnProtection.EnvelopeExtensionData["providerMetadata"]
            .GetProperty("revision").GetInt32());
        Assert.Equal("strict", model.SiegeMode.EnvelopeExtensionData["futureSiegePolicy"]
            .GetProperty("mode").GetString());
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_PreservesRuleOverrideEnvelopeExtensionData()
    {
        var vpnConfiguration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabledOverride": "Inherit",
              "inheritGlobalRules": true,
              "rules": [],
              "ruleOverrides": [],
              "providerMetadata": { "source": "saved-server", "revision": 3 }
            }
            """);
        var siegeConfiguration = BuildConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "siege-mode",
              "enabledOverride": "Inherit",
              "inheritGlobalRules": true,
              "rules": [],
              "ruleOverrides": [],
              "futureSiegePolicy": { "source": "saved-server", "mode": "relaxed" }
            }
            """);
        var model = new GameServerEditViewModel();

        parser.PopulateGameServerSettingsViewModel(model, vpnConfiguration, logger);
        parser.PopulateGameServerSettingsViewModel(model, siegeConfiguration, logger);

        Assert.Equal("saved-server", model.VpnProtection.EnvelopeExtensionData["providerMetadata"]
            .GetProperty("source").GetString());
        Assert.Equal(3, model.VpnProtection.EnvelopeExtensionData["providerMetadata"]
            .GetProperty("revision").GetInt32());
        Assert.Equal("relaxed", model.SiegeMode.EnvelopeExtensionData["futureSiegePolicy"]
            .GetProperty("mode").GetString());
    }

    [Fact]
    public void RuleSettingsEnvelopeData_CopiesOnlyAuthoritativeExtensionData()
    {
        var savedGlobal = new GlobalSettingsViewModel();
        savedGlobal.VpnProtection.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"savedGlobal":{"source":"repository"}}""")!;
        savedGlobal.SiegeMode.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"savedSiege":{"source":"repository"}}""")!;
        var postedGlobal = new GlobalSettingsViewModel();
        postedGlobal.VpnProtection.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"clientValue":{"source":"form"}}""")!;
        postedGlobal.SiegeMode.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"clientValue":{"source":"form"}}""")!;

        RuleSettingsEnvelopeData.CopyFromAuthoritative(savedGlobal, postedGlobal);

        Assert.Contains("savedGlobal", postedGlobal.VpnProtection.EnvelopeExtensionData);
        Assert.DoesNotContain("clientValue", postedGlobal.VpnProtection.EnvelopeExtensionData);
        Assert.Contains("savedSiege", postedGlobal.SiegeMode.EnvelopeExtensionData);
        Assert.DoesNotContain("clientValue", postedGlobal.SiegeMode.EnvelopeExtensionData);

        var savedServer = new GameServerEditViewModel();
        savedServer.VpnProtection.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"savedServer":{"source":"repository"}}""")!;
        savedServer.SiegeMode.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"savedSiegeServer":{"source":"repository"}}""")!;
        var postedServer = new GameServerEditViewModel();
        postedServer.VpnProtection.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"clientValue":{"source":"form"}}""")!;
        postedServer.SiegeMode.EnvelopeExtensionData =
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                /*lang=json,strict*/ """{"clientValue":{"source":"form"}}""")!;

        RuleSettingsEnvelopeData.CopyFromAuthoritative(savedServer, postedServer);

        Assert.Contains("savedServer", postedServer.VpnProtection.EnvelopeExtensionData);
        Assert.DoesNotContain("clientValue", postedServer.VpnProtection.EnvelopeExtensionData);
        Assert.Contains("savedSiegeServer", postedServer.SiegeMode.EnvelopeExtensionData);
        Assert.DoesNotContain("clientValue", postedServer.SiegeMode.EnvelopeExtensionData);
    }

    [Fact]
    public void TryParseVpnProtectionSettings_InvalidFeatureDocument_FailsClosed()
    {
        var config = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {"schemaVersion":1,"featureId":"siege-mode","enabled":false,"rules":[]}
            """);

        Assert.False(parser.TryParseVpnProtectionSettings(config, out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TryParseSiegeModeSettings_ValidDocument_ReturnsTypedSettings()
    {
        var configuration = BuildConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "featureId": "siege-mode",
          "enabled": true,
          "durationMinutes": 240,
          "rules": [
            {
              "id": "new-guid",
              "enabled": true,
              "order": 0,
              "condition": {
                "conditionId": "player.first-seen-guid",
                "parameters": {
                  "operator": "IsNew",
                  "referenceScope": "SiegeBaseline"
                }
              },
              "actions": [{
                "id": "kick",
                "typeId": "player.kick",
                "parameters": {"reason": "Siege Mode"},
                "dependsOnActionIds": []
              }],
              "afterMatch": "StopProcessingRules"
            }
          ]
        }
        """);

        var result = parser.TryParseSiegeModeSettings(configuration, out var settings, out var errors);

        Assert.True(result);
        Assert.Empty(errors);
        Assert.NotNull(settings);
        Assert.True(settings.Enabled);
        Assert.Equal("siege-mode", settings.FeatureId);
        Assert.Equal(240, settings.DurationMinutes);
        Assert.Equal("new-guid", Assert.Single(settings.Rules).Id);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_SiegeMode_MapsDurationAndNestedConditions()
    {
        var model = new GlobalSettingsViewModel();
        var configuration = BuildConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "featureId": "siege-mode",
          "enabled": true,
          "durationMinutes": 75,
          "rules": [
            {
              "id": "history-and-vpn",
              "enabled": true,
              "order": 0,
              "condition": {
                "groupOperator": "And",
                "conditions": [
                  {
                    "conditionId": "player.first-seen-ip",
                    "parameters": {
                      "operator": "IsNew",
                      "referenceScope": "SiegeBaseline"
                    }
                  },
                  {
                    "groupOperator": "Or",
                    "conditions": [
                      {
                        "conditionId": "vpn.intelligence",
                        "parameters": {
                          "signal": "ProxyCheckIsVpn",
                          "operator": "Equal",
                          "expectedValue": "true"
                        }
                      },
                      {
                        "conditionId": "vpn.intelligence",
                        "parameters": {
                          "signal": "MaxMindIsAnonymousVpn",
                          "operator": "Equal",
                          "expectedValue": "true"
                        }
                      }
                    ]
                  }
                ]
              },
              "actions": [{
                "id": "kick",
                "typeId": "player.kick",
                "parameters": {"reason": "Siege Mode"},
                "dependsOnActionIds": []
              }],
              "afterMatch": "StopProcessingRules"
            }
          ]
        }
        """);

        parser.PopulateGlobalSettingsViewModel(model, configuration, logger);

        Assert.True(model.SiegeMode.Enabled);
        Assert.Equal(75, model.SiegeMode.DurationMinutes);
        using var rules = JsonDocument.Parse(model.SiegeMode.RulesJson);
        var condition = rules.RootElement[0].GetProperty("condition");
        Assert.Equal("And", condition.GetProperty("groupOperator").GetString());
        Assert.Equal("player.first-seen-ip", condition.GetProperty("conditions")[0].GetProperty("conditionId").GetString());
        Assert.Equal("Or", condition.GetProperty("conditions")[1].GetProperty("groupOperator").GetString());
        Assert.Equal("vpn.intelligence", condition.GetProperty("conditions")[1].GetProperty("conditions")[0].GetProperty("conditionId").GetString());
    }

    [Theory]
    [InlineData("siegeMode", "{")]
    [InlineData("other", /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"siege-mode","enabled":false,"durationMinutes":240,"rules":[]}""")]
    [InlineData("siegeMode", /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"siege-mode","enabled":false,"durationMinutes":0,"rules":[]}""")]
    public void TryParseSiegeModeSettings_InvalidDocument_FailsClosed(
        string configurationNamespace,
        string configurationJson)
    {
        var configuration = BuildConfiguration(configurationNamespace, configurationJson);

        var result = parser.TryParseSiegeModeSettings(configuration, out var settings, out var errors);

        Assert.False(result);
        Assert.Null(settings);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_VpnProtection_MapsUniversalRules()
    {
        var model = new GlobalSettingsViewModel();
        var configuration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "featureId": "vpn-protection",
          "enabled": true,
          "rules": [
            {
              "id": "vpn",
              "enabled": true,
              "order": 0,
              "condition": {
                "conditionId": "vpn.intelligence",
                "parameters": {
                  "signal": "ProxyCheckIsVpn",
                  "operator": "Equal",
                  "expectedValue": "true"
                }
              },
              "actions": [{
                "id": "ban",
                "typeId": "moderation.ban",
                "parameters": {
                  "reason": "VPN Protection {ruleId}",
                  "postToForums": true
                },
                "dependsOnActionIds": []
              }],
              "afterMatch": "StopProcessingRules"
            }
          ]
        }
        """);

        parser.PopulateGlobalSettingsViewModel(model, configuration, logger);

        Assert.True(model.VpnProtection.Enabled);
        using var rules = JsonDocument.Parse(model.VpnProtection.RulesJson);
        var rule = Assert.Single(rules.RootElement.EnumerateArray().ToArray());
        Assert.Equal("vpn", rule.GetProperty("id").GetString());
        Assert.Equal("vpn.intelligence", rule.GetProperty("condition").GetProperty("conditionId").GetString());
        Assert.Equal("moderation.ban", rule.GetProperty("actions")[0].GetProperty("typeId").GetString());
        Assert.True(rule.GetProperty("actions")[0].GetProperty("parameters").GetProperty("postToForums").GetBoolean());
    }

    [Theory]
    [InlineData("Inherit", null)]
    [InlineData("Enabled", true)]
    [InlineData("Disabled", false)]
    public void PopulateGameServerSettingsViewModel_VpnProtection_MapsUniversalOverrides(
        string enabledOverride, bool? expectedEnabled)
    {
        var model = new GameServerEditViewModel();
        var configuration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, $$"""
        {
          "schemaVersion": 1,
          "featureId": "vpn-protection",
          "enabledOverride": "{{enabledOverride}}",
          "inheritGlobalRules": true,
          "rules": [{
            "id": "local",
            "enabled": true,
            "order": 0,
            "condition": {
              "conditionId": "vpn.intelligence",
              "parameters": {
                "signal": "MaxMindIsTorExitNode",
                "operator": "Equal",
                "expectedValue": "true"
              }
            },
            "actions": [{
              "id": "observe",
              "typeId": "moderation.observation",
              "parameters": {"reason": "Local rule"},
              "dependsOnActionIds": []
            }],
            "afterMatch": "StopProcessingRules"
          }],
          "ruleOverrides": [{
            "ruleId": "vpn",
            "enabled": false,
            "condition": null,
            "actions": null,
            "afterMatch": null
          }]
        }
        """);

        parser.PopulateGameServerSettingsViewModel(model, configuration, logger);

        Assert.Equal(expectedEnabled, model.VpnProtection.Enabled);
        Assert.True(model.VpnProtection.InheritGlobalRules);
        using var localRules = JsonDocument.Parse(model.VpnProtection.LocalRulesJson);
        Assert.Equal("local", localRules.RootElement[0].GetProperty("id").GetString());
        using var overrides = JsonDocument.Parse(model.VpnProtection.RuleOverridesJson);
        Assert.Equal("vpn", overrides.RootElement[0].GetProperty("ruleId").GetString());
        Assert.False(overrides.RootElement[0].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_SiegeMode_MapsUniversalDurationOverride()
    {
        var model = new GameServerEditViewModel();
        var configuration = BuildConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "siege-mode",
              "enabledOverride": "Disabled",
              "inheritGlobalRules": false,
              "durationMinutesOverride": 120,
              "rules": [],
              "ruleOverrides": []
            }
            """);

        parser.PopulateGameServerSettingsViewModel(model, configuration, logger);

        Assert.False(model.SiegeMode.Enabled);
        Assert.False(model.SiegeMode.InheritGlobalRules);
        Assert.Equal(120, model.SiegeMode.DurationMinutesOverride);
        Assert.Equal("[]", model.SiegeMode.LocalRulesJson);
        Assert.Equal("[]", model.SiegeMode.RuleOverridesJson);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_VpnProtection_PreservesUniversalRuleActionParameters()
    {
        var model = new GlobalSettingsViewModel();
        var configuration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [{
                "id": "vpn",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": {
                    "signal": "ProxyCheckIsVpn",
                    "operator": "Equal",
                    "expectedValue": "true"
                  }
                },
                "actions": [{
                  "id": "observe",
                  "typeId": "moderation.observation",
                  "parameters": {
                    "reason": "VPN Protection {ruleId}",
                    "postToForums": false
                  },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }]
            }
            """);

        parser.PopulateGlobalSettingsViewModel(model, configuration, logger);

        using var rules = JsonDocument.Parse(model.VpnProtection.RulesJson);
        Assert.False(rules.RootElement[0].GetProperty("actions")[0].GetProperty("parameters").GetProperty("postToForums").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UniversalGlobalRule_RoundTripsUnknownConditionAndActionExtensions(bool isVpn)
    {
        var featureId = isVpn ? "vpn-protection" : "siege-mode";
        var settingsNamespace = isVpn ? VpnProtectionSettingsConstants.Namespace : SiegeModeSettingsConstants.Namespace;
        var configuration = BuildConfiguration(settingsNamespace, $$"""
            {
              "schemaVersion": 1,
              "featureId": "{{featureId}}",
              "enabled": true,
              "durationMinutes": 60,
              "rules": [{
                "id": "future-condition",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "future.condition",
                  "parameters": {"threshold": 17},
                  "futureConditionMetadata": {"label": "keep"}
                },
                "actions": [{
                  "id": "observe",
                  "typeId": "moderation.observation",
                  "parameters": {"reason": "Future condition", "futureParameter": ["one", "two"]},
                  "dependsOnActionIds": [],
                  "futureActionMetadata": true
                }],
                "afterMatch": "ContinueToNextRule"
              }]
            }
            """);
        var model = new GlobalSettingsViewModel();

        parser.PopulateGlobalSettingsViewModel(model, configuration, logger);
        var (_, json) = Assert.Single(new NamespaceSettingsSerializer().BuildGlobalSettingsConfigurations(model),
            item => item.Namespace == settingsNamespace);
        var roundTrip = BuildConfiguration(settingsNamespace, json);

        if (isVpn)
            Assert.True(parser.TryParseVpnProtectionSettings(roundTrip, out _, out var errors), string.Join("; ", errors));
        else
            Assert.True(parser.TryParseSiegeModeSettings(roundTrip, out _, out var errors), string.Join("; ", errors));

        using var document = JsonDocument.Parse(json);
        var rule = document.RootElement.GetProperty("rules")[0];
        var condition = rule.GetProperty("condition");
        Assert.Equal("future.condition", condition.GetProperty("conditionId").GetString());
        Assert.Equal(17, condition.GetProperty("parameters").GetProperty("threshold").GetInt32());
        Assert.Equal("keep", condition.GetProperty("futureConditionMetadata").GetProperty("label").GetString());
        var action = rule.GetProperty("actions")[0];
        Assert.Equal("two", action.GetProperty("parameters").GetProperty("futureParameter")[1].GetString());
        Assert.True(action.GetProperty("futureActionMetadata").GetBoolean());
        Assert.Equal("ContinueToNextRule", rule.GetProperty("afterMatch").GetString());
    }

    [Fact]
    public void PopulateGameServerDetails_ActiveTransportAndRcon_MapsViewDataValues()
    {
        var viewData = new Dictionary<string, object?>();

        var sftpConfig = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "hostname": "sftp.example.com",
          "port": 22,
          "username": "ops-user",
          "password": "sftp-secret"
        }
        """);

        var rconConfig = BuildConfiguration("rcon", /*lang=json,strict*/ """
        {
          "password": "rcon-secret"
        }
        """);

        parser.PopulateGameServerDetails(viewData, FileTransportType.Sftp, sftpConfig, logger);
        parser.PopulateGameServerDetails(viewData, FileTransportType.Sftp, rconConfig, logger);

        Assert.Equal("sftp.example.com", viewData["FtpHostname"]);
        Assert.Equal(22, viewData["FtpPort"]);
        Assert.Equal("ops-user", viewData["FtpUsername"]);
        Assert.Equal("sftp-secret", viewData["FtpPassword"]);
        Assert.Equal(SftpAuthenticationType.Password, viewData["SftpAuthenticationType"]);
        Assert.Equal(FileTransportType.Sftp, viewData["FileTransportType"]);
        Assert.Equal("rcon-secret", viewData["RconPassword"]);
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_CredentialNamespaces_MapEditableValues()
    {
        var sftpModel = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                FileTransportType = FileTransportType.Sftp
            }
        };
        var sftpConfig = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "hostname": "sftp.example.com",
          "port": 22,
          "username": "ops-user",
          "password": "sftp-secret",
          "hostKeyFingerprint": "aa:bb:cc"
        }
        """);
        var rconConfig = BuildConfiguration("rcon", /*lang=json,strict*/ """
        {
          "password": "rcon-secret"
        }
        """);

        parser.PopulateGameServerSettingsViewModel(sftpModel, sftpConfig, logger);
        parser.PopulateGameServerSettingsViewModel(sftpModel, rconConfig, logger);

        Assert.Equal("sftp-secret", sftpModel.FileTransportConfigPassword);
        Assert.Equal(SftpAuthenticationType.Password, sftpModel.FileTransportConfigSftpAuthenticationType);
        Assert.Equal("rcon-secret", sftpModel.RconConfigPassword);

        var ftpModel = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                FileTransportType = FileTransportType.Ftp
            }
        };
        var ftpConfig = BuildConfiguration("ftp", /*lang=json,strict*/ """
        {
          "hostname": "ftp.example.com",
          "port": 21,
          "username": "ops-user",
          "password": "ftp-secret"
        }
        """);

        parser.PopulateGameServerSettingsViewModel(ftpModel, ftpConfig, logger);

        Assert.Equal("ftp-secret", ftpModel.FileTransportConfigPassword);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void PopulateGameServerDetails_SftpPrivateKey_MapsCredentials(
        bool hasPrivateKey,
        bool hasPassphrase)
    {
        var viewData = new Dictionary<string, object?>();
        var configuration = BuildConfiguration("sftp", System.Text.Json.JsonSerializer.Serialize(new
        {
            authenticationType = "PrivateKey",
            privateKey = hasPrivateKey ? "private-key-content" : null,
            privateKeyPassphrase = hasPassphrase ? "key-passphrase" : null
        }));

        parser.PopulateGameServerDetails(viewData, FileTransportType.Sftp, configuration, logger);

        Assert.Equal(SftpAuthenticationType.PrivateKey, viewData["SftpAuthenticationType"]);
        Assert.Equal(hasPrivateKey ? "private-key-content" : null, viewData["SftpPrivateKey"]);
        Assert.Equal(hasPassphrase ? "key-passphrase" : null, viewData["SftpPrivateKeyPassphrase"]);
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_SftpPrivateKey_MapsCredentials()
    {
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                FileTransportType = FileTransportType.Sftp
            }
        };
        var configuration = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "hostname": "sftp.example.com",
          "port": 22,
          "username": "ops-user",
          "authenticationType": "PrivateKey",
          "privateKey": "private-key-content",
          "privateKeyPassphrase": "key-passphrase",
          "hostKeyFingerprint": "aa:bb:cc"
        }
        """);

        parser.PopulateGameServerSettingsViewModel(model, configuration, logger);

        Assert.Equal(SftpAuthenticationType.PrivateKey, model.FileTransportConfigSftpAuthenticationType);
        Assert.Equal("private-key-content", model.FileTransportConfigPrivateKey);
        Assert.Equal("key-passphrase", model.FileTransportConfigPrivateKeyPassphrase);
    }

    [Fact]
    public void PopulateExistingCredentials_BlankCredentialFields_ArePreservedFromCurrentNamespaces()
    {
        var model = new GameServerEditViewModel
        {
            FileTransportConfigPassword = null,
            FileTransportConfigHostKeyFingerprint = null,
            RconConfigPassword = null
        };

        var sftpConfig = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "authenticationType": "PrivateKey",
          "password": "existing-sftp-password",
          "privateKey": "existing-private-key",
          "privateKeyPassphrase": "existing-passphrase",
          "hostKeyFingerprint": "aa:bb:cc"
        }
        """);

        var rconConfig = BuildConfiguration("rcon", /*lang=json,strict*/ """
        {
          "password": "existing-rcon-password"
        }
        """);

        parser.PopulateExistingCredentials(
            model,
            activeTransportNamespace: "sftp",
            sftpConfig,
            new CredentialPreservationOptions(true, true, true, true, true),
            logger);

        parser.PopulateExistingCredentials(
            model,
            activeTransportNamespace: "sftp",
            rconConfig,
            new CredentialPreservationOptions(true, true, true, true, true),
            logger);

        Assert.Equal("existing-sftp-password", model.FileTransportConfigPassword);
        Assert.Equal("existing-private-key", model.FileTransportConfigPrivateKey);
        Assert.Equal("existing-passphrase", model.FileTransportConfigPrivateKeyPassphrase);
        Assert.Equal("aa:bb:cc", model.FileTransportConfigHostKeyFingerprint);
        Assert.Equal("existing-rcon-password", model.RconConfigPassword);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_Cod4xNamespaces_MapToModel()
    {
        var model = new GlobalSettingsViewModel();

        var pluginConfig = BuildConfiguration(Cod4xPluginSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "vpnProtectionEnabled": true,
          "pluginRootDirectory": "/plugins"
        }
        """);

        var powerConfig = BuildConfiguration(Cod4xPowerSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "defaultPower": 55,
          "tagMappings": [
            { "tag": "SeniorAdmin", "power": 100, "enabled": true }
          ]
        }
        """);

        var commandConfig = BuildConfiguration(Cod4xCommandSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "commands": {
            "say": { "enabled": false, "minPower": 77 }
          }
        }
        """);

        parser.PopulateGlobalSettingsViewModel(model, pluginConfig, logger);
        parser.PopulateGlobalSettingsViewModel(model, powerConfig, logger);
        parser.PopulateGlobalSettingsViewModel(model, commandConfig, logger);

        Assert.True(model.Cod4xPluginEnabled);
        Assert.True(model.Cod4xPluginVpnProtectionEnabled);
        Assert.Equal("/plugins", model.Cod4xPluginRootDirectory);
        Assert.True(model.Cod4xPowerEnabled);
        Assert.Equal(55, model.Cod4xPowerDefaultPower);
        Assert.Contains("SeniorAdmin", model.Cod4xPowerTagMappingsJson, StringComparison.Ordinal);

        Assert.True(model.Cod4xCommandsEnabled);
        var sayCommand = Assert.Single(model.Cod4xCommands, static command => string.Equals(command.Name, "say", StringComparison.OrdinalIgnoreCase));
        Assert.False(sayCommand.Enabled);
        Assert.Equal(77, sayCommand.MinPower);
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_Cod4xNamespaces_DisablesInheritAndAppliesOverrides()
    {
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameType = GameType.CallOfDuty4x
            }
        };

        var pluginConfig = BuildConfiguration(Cod4xPluginSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "vpnProtectionEnabled": true,
          "pluginRootDirectory": "/servers/cod4x/plugins",
          "runtimeState": {
            "currentVersion": "1.2.3",
            "previousKnownGoodVersion": "1.2.2",
            "lastOperationId": "op-123",
            "lastOperationStatus": "Succeeded",
            "lastOperationUtc": "2026-01-01T12:00:00Z",
            "lastError": "none"
          },
          "operationRequest": {
            "operationId": "op-124",
            "action": "Install",
            "targetVersion": "1.2.4",
            "requestedAtUtc": "2026-01-01T13:00:00Z",
            "requestedBy": "tester"
          }
        }
        """);

        var powerConfig = BuildConfiguration(Cod4xPowerSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "defaultPower": 60,
          "tagMappings": [
            { "tag": "Moderator", "power": 40, "enabled": true }
          ]
        }
        """);

        var commandConfig = BuildConfiguration(Cod4xCommandSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "commands": {
            "kick": { "enabled": false, "minPower": 88 }
          }
        }
        """);

        parser.PopulateGameServerSettingsViewModel(model, pluginConfig, logger);
        parser.PopulateGameServerSettingsViewModel(model, powerConfig, logger);
        parser.PopulateGameServerSettingsViewModel(model, commandConfig, logger);

        Assert.False(model.Cod4xInheritPluginSettings);
        Assert.False(model.Cod4xInheritPowerSettings);
        Assert.False(model.Cod4xInheritCommandSettings);

        Assert.True(model.Cod4xPluginEnabled);
        Assert.True(model.Cod4xPluginVpnProtectionEnabled);
        Assert.Equal("/servers/cod4x/plugins", model.Cod4xPluginRootDirectory);
        Assert.Equal("1.2.3", model.Cod4xRuntimeCurrentVersion);
        Assert.Equal("1.2.2", model.Cod4xRuntimePreviousKnownGoodVersion);
        Assert.Equal("op-123", model.Cod4xRuntimeLastOperationId);
        Assert.Equal(Cod4xPluginOperationStatus.Succeeded, model.Cod4xRuntimeLastOperationStatus);
        Assert.Equal("none", model.Cod4xRuntimeLastError);
        Assert.Equal("op-124", model.Cod4xOperationRequestOperationId);
        Assert.Equal(Cod4xPluginOperationAction.Install, model.Cod4xOperationRequestAction);
        Assert.Equal("1.2.4", model.Cod4xOperationRequestTargetVersion);
        Assert.Equal("tester", model.Cod4xOperationRequestRequestedBy);
        Assert.True(model.Cod4xPowerEnabled);
        Assert.Equal(60, model.Cod4xPowerDefaultPower);
        Assert.Contains("Moderator", model.Cod4xPowerTagMappingsJson, StringComparison.Ordinal);

        Assert.True(model.Cod4xCommandsEnabled);
        var kickCommand = Assert.Single(model.Cod4xCommands, static command => string.Equals(command.Name, "kick", StringComparison.OrdinalIgnoreCase));
        Assert.False(kickCommand.Enabled);
        Assert.Equal(88, kickCommand.MinPower);
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_Cod4xPluginLifecycleOnly_PreservesInheritAndMapsRuntimeState()
    {
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameType = GameType.CallOfDuty4x
            }
        };

        var pluginConfig = BuildConfiguration(Cod4xPluginSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "runtimeState": {
            "currentVersion": "1.2.3",
            "lastOperationId": "op-123",
            "lastOperationStatus": "Succeeded"
          },
          "operationRequest": {
            "operationId": "op-124",
            "action": "Install",
            "targetVersion": "1.2.4",
            "requestedBy": "tester"
          }
        }
        """);

        parser.PopulateGameServerSettingsViewModel(model, pluginConfig, logger);

        Assert.True(model.Cod4xInheritPluginSettings);
        Assert.False(model.Cod4xPluginEnabled);
        Assert.Equal("1.2.3", model.Cod4xRuntimeCurrentVersion);
        Assert.Equal("op-123", model.Cod4xRuntimeLastOperationId);
        Assert.Equal(Cod4xPluginOperationStatus.Succeeded, model.Cod4xRuntimeLastOperationStatus);
        Assert.Equal("op-124", model.Cod4xOperationRequestOperationId);
        Assert.Equal(Cod4xPluginOperationAction.Install, model.Cod4xOperationRequestAction);
        Assert.Equal("1.2.4", model.Cod4xOperationRequestTargetVersion);
        Assert.Equal("tester", model.Cod4xOperationRequestRequestedBy);
    }

    [Fact]
    public void PopulateGlobalSettingsViewModel_Cod4xPower_AppliedAfterRequiredTags_UsesJsonValues()
    {
        var model = new GlobalSettingsViewModel();
        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "SeniorAdmin", DisplayName = "SeniorAdmin" },
            new RequiredTagOptionViewModel { Name = "Moderator", DisplayName = "Moderator" }
        ]);

        var powerConfig = BuildConfiguration(Cod4xPowerSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "defaultPower": 55,
          "tagMappings": [
            { "tag": "SeniorAdmin", "power": 100, "enabled": true }
          ]
        }
        """);

        parser.PopulateGlobalSettingsViewModel(model, powerConfig, logger);

        var seniorAdmin = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "SeniorAdmin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100, seniorAdmin.Power);

        var moderator = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, moderator.Power);
    }

    [Fact]
    public void PopulateGameServerSettingsViewModel_Cod4xPower_AppliedAfterRequiredTags_UsesJsonValues()
    {
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameType = GameType.CallOfDuty4x
            }
        };

        model.ApplyAvailableRequiredTags(
        [
            new RequiredTagOptionViewModel { Name = "Moderator", DisplayName = "Moderator" },
            new RequiredTagOptionViewModel { Name = "HeadAdmin", DisplayName = "HeadAdmin" }
        ]);

        var powerConfig = BuildConfiguration(Cod4xPowerSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "defaultPower": 60,
          "tagMappings": [
            { "tag": "Moderator", "power": 40, "enabled": true }
          ]
        }
        """);

        parser.PopulateGameServerSettingsViewModel(model, powerConfig, logger);

        var moderator = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(40, moderator.Power);

        var headAdmin = Assert.Single(model.Cod4xPowerTagMappings, static mapping => string.Equals(mapping.Tag, "HeadAdmin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, headAdmin.Power);
    }

    private static ConfigurationDto BuildConfiguration(string ns, string configuration)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = ns,
            Configuration = configuration
        }))!;
    }
}
