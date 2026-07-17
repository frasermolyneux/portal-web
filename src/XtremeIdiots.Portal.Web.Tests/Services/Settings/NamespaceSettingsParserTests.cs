using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class NamespaceSettingsParserTests
{
  private readonly NamespaceSettingsParser parser = new();
  private readonly ILogger logger = Mock.Of<ILogger>();

  [Fact]
  public void PopulateGlobalSettingsViewModel_VpnProtection_MapsRulesAndExcludedTags()
  {
    var model = new GlobalSettingsViewModel();
    var configuration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": true,
          "rules": [
            {
              "id": "vpn",
              "signal": "ProxyCheckIsVpn",
              "operator": "Equal",
              "expectedValue": "true",
              "action": "Ban",
              "reasonTemplate": "VPN Protection {ruleId}"
            }
          ],
          "excludedPlayerTags": ["Trusted VPN"]
        }
        """);

    parser.PopulateGlobalSettingsViewModel(model, configuration, logger);

    Assert.True(model.VpnProtection.Enabled);
    var rule = Assert.Single(model.VpnProtection.Rules);
    Assert.Equal("vpn", rule.Id);
    Assert.Equal(VpnProtectionAction.Ban, rule.Action);
    Assert.Equal("Trusted VPN", model.VpnProtection.ExcludedPlayerTagsCsv);
  }

  [Fact]
  public void PopulateGameServerSettingsViewModel_VpnProtection_MapsOverridesAndLocalRules()
  {
    var model = new GameServerEditViewModel();
    var configuration = BuildConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
        {
          "schemaVersion": 1,
          "enabled": false,
          "inheritGlobalRules": true,
          "rules": [
            {
              "id": "local",
              "signal": "MaxMindIsTorExitNode",
              "operator": "Equal",
              "expectedValue": "true",
              "action": "Kick"
            }
          ],
          "ruleOverrides": [
            { "id": "vpn", "action": "Observation" }
          ],
          "excludedPlayerTags": ["Server Exempt"]
        }
        """);

    parser.PopulateGameServerSettingsViewModel(model, configuration, logger);

    Assert.False(model.VpnProtection.Enabled);
    Assert.True(model.VpnProtection.InheritGlobalRules);
    Assert.Single(model.VpnProtection.LocalRules);
    Assert.Equal(VpnProtectionAction.Observation, Assert.Single(model.VpnProtection.RuleOverrides).Action);
    Assert.Equal("Server Exempt", model.VpnProtection.ExcludedPlayerTagsCsv);
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
    Assert.Equal(FileTransportType.Sftp, viewData["FileTransportType"]);
    Assert.Equal("rcon-secret", viewData["RconPassword"]);
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
          "password": "existing-sftp-password",
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
        needsFileTransportPassword: true,
        needsFileTransportHostKeyFingerprint: true,
        needsRconPassword: true,
        logger);

    parser.PopulateExistingCredentials(
        model,
        activeTransportNamespace: "sftp",
        rconConfig,
        needsFileTransportPassword: true,
        needsFileTransportHostKeyFingerprint: true,
        needsRconPassword: true,
        logger);

    Assert.Equal("existing-sftp-password", model.FileTransportConfigPassword);
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
