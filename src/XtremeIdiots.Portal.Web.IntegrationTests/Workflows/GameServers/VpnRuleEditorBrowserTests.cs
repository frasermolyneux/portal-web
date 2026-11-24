using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Trait("Category", "Browser")]
public sealed class VpnRuleEditorBrowserTests
{
    [Fact]
    public async Task ServerEditor_SeparatesFeatureEnablementFromRuleInheritance_AndPreservesEntries()
    {
        var scenario = new FileTransportScenario();
        var global = Configuration(/*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [{
                "id": "global-vpn",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": { "signal": "ProxyCheckIsVpn", "operator": "Equal", "expectedValue": "true" }
                },
                "actions": [{
                  "id": "observe-global",
                  "typeId": "player.kick",
                  "parameters": { "reason": "global VPN" },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }]
            }
            """);
        var server = Configuration(/*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabledOverride": "Disabled",
              "inheritGlobalRules": false,
              "rules": [{
                "id": "local-vpn",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": {
                    "signal": "MaxMindIsAnonymous",
                    "operator": "Equal",
                    "expectedValue": "true",
                    "legacySignalMetadata": "preserve-local"
                  }
                },
                "actions": [{
                  "id": "observe-local",
                  "typeId": "moderation.observation",
                  "parameters": { "reason": "local VPN", "legacyActionMetadata": "preserve-action" },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }],
              "ruleOverrides": [{
                "ruleId": "global-vpn",
                "enabled": false,
                "condition": null,
                "actions": null,
                "afterMatch": null
              }]
            }
            """);
        Mock.Get(scenario.RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([global]))));
        Mock.Get(scenario.RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([scenario.SftpConfiguration, server]))));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            scenario.ConfigureServices(services);
            services.RemoveAll<IGameServerSettingsService>();
            services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        });
        var page = browser.Page;
        var path = $"/GameServers/Edit/{scenario.GameServerId}";
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, path).AbsoluteUri);
        await Assertions.Expect(page.Locator("#vpn-protection-tab-btn")).ToBeVisibleAsync();
        await page.Locator("#vpn-protection-tab-btn").ClickAsync();

        var enabled = page.Locator("#VpnProtection_Enabled");
        var inheritRules = page.Locator("#VpnProtection_InheritGlobalRules");
        await Assertions.Expect(enabled).ToHaveValueAsync("false");
        await Assertions.Expect(inheritRules).Not.ToBeCheckedAsync();

        var overrides = page.Locator("#VpnProtection-rule-overrides [data-rules-editor]");
        await Assertions.Expect(overrides).ToContainTextAsync("Inherited global rules");
        await Assertions.Expect(overrides).ToContainTextAsync("unknown; server capability evidence is unavailable");
        var localRules = page.Locator("#VpnProtection-local-rules [data-rules-editor] > section");
        await Assertions.Expect(localRules).ToContainTextAsync("supported for this server");
        await Assertions.Expect(localRules).ToContainTextAsync("Additional preserved parameters");
        var existingActionType = localRules.Locator("label").Filter(new() { HasText = "Action type" }).Locator("select").First;
        await Assertions.Expect(existingActionType.Locator("option[value='moderation.observation']")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#VpnProtection-local-rules [data-rules-editor]"))
            .Not.ToContainTextAsync("New conditions and actions require verified server compatibility");

        await localRules.GetByRole(AriaRole.Button, new() { Name = "Add action" }).ClickAsync();
        var actionTypes = localRules.Locator("label").Filter(new() { HasText = "Action type" }).Locator("select");
        await Assertions.Expect(actionTypes).ToHaveCountAsync(2);
        await Assertions.Expect(actionTypes.Nth(1).Locator("option[value='moderation.observation']")).ToHaveCountAsync(1);
        await Assertions.Expect(actionTypes.Nth(1).Locator("option[value='player.kick']")).ToHaveCountAsync(0);
        await localRules.GetByRole(AriaRole.Button, new() { Name = "Remove action" }).Nth(1).ClickAsync();

        await enabled.SelectOptionAsync("");
        await Assertions.Expect(enabled).ToHaveValueAsync("");
        await Assertions.Expect(inheritRules).Not.ToBeCheckedAsync();
        await inheritRules.CheckAsync();
        await Assertions.Expect(enabled).ToHaveValueAsync("");

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("game-server-save").ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(302, response.Status);

        var command = Assert.Single(scenario.UpsertedConfigurations, entry => entry.Namespace == VpnProtectionSettingsConstants.Namespace);
        using var saved = JsonDocument.Parse(command.Configuration);
        Assert.Equal("Inherit", saved.RootElement.GetProperty("enabledOverride").GetString());
        Assert.True(saved.RootElement.GetProperty("inheritGlobalRules").GetBoolean());
        var localRule = saved.RootElement.GetProperty("rules")[0];
        Assert.Equal("local-vpn", localRule.GetProperty("id").GetString());
        Assert.Equal("MaxMindIsAnonymous", localRule.GetProperty("condition").GetProperty("parameters").GetProperty("signal").GetString());
        Assert.Equal("preserve-local", localRule.GetProperty("condition").GetProperty("parameters").GetProperty("legacySignalMetadata").GetString());
        Assert.Equal("preserve-action", localRule.GetProperty("actions")[0].GetProperty("parameters").GetProperty("legacyActionMetadata").GetString());
        var ruleOverride = saved.RootElement.GetProperty("ruleOverrides")[0];
        Assert.Equal("global-vpn", ruleOverride.GetProperty("ruleId").GetString());
        Assert.False(ruleOverride.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ruleOverride.GetProperty("condition").ValueKind);
        Assert.Equal(JsonValueKind.Null, ruleOverride.GetProperty("actions").ValueKind);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task ServerEditor_FreshSnapshotEnablesReportedChoicesAndFiltersMissingCapabilities()
    {
        var scenario = new FileTransportScenario();
        var gameServerJson = JObject.FromObject(scenario.GameServer);
        gameServerJson[nameof(scenario.GameServer.GameType)] = GameType.CallOfDuty4x.ToString();
        var gameServer = gameServerJson.ToObject<GameServerDto>()!;
        Mock.Get(scenario.RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(
                HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));
        var global = Configuration(/*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [{
                "id": "global-public-message",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": { "signal": "ProxyCheckIsVpn", "operator": "Equal", "expectedValue": "true" }
                },
                "actions": [{
                  "id": "public-message",
                  "typeId": "message.public",
                  "parameters": { "message": "Test message" },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }]
            }
            """);
        var server = Configuration(/*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabledOverride": "Inherit",
              "inheritGlobalRules": true,
              "rules": [],
              "ruleOverrides": []
            }
            """);
        Mock.Get(scenario.RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(
                    new CollectionModel<ConfigurationDto>([global]))));
        Mock.Get(scenario.RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(
                    new CollectionModel<ConfigurationDto>([scenario.SftpConfiguration, server]))));
        var snapshot = new RuleServerCapabilitiesDto
        {
            GameServerId = scenario.GameServerId,
            GameType = GameType.CallOfDuty4x,
            HasFreshStatus = true,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            IntegrationCapabilities = [RulesEngineCatalogueV1.RconPlayerDropCapability]
        };
        Mock.Get(scenario.RepositoryClient.Object.RuleConnectionBindings.V1)
            .Setup(api => api.GetCurrentCapabilities(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleServerCapabilitiesDto>(
                HttpStatusCode.OK, new ApiResponse<RuleServerCapabilitiesDto>(snapshot)));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            scenario.ConfigureServices(services);
            services.RemoveAll<IGameServerSettingsService>();
            services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        });
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/GameServers/Edit/{scenario.GameServerId}").AbsoluteUri);
        await page.Locator("#vpn-protection-tab-btn").ClickAsync();

        var localEditor = page.Locator("#VpnProtection-local-rules [data-rules-editor]");
        await localEditor.GetByRole(AriaRole.Button, new() { Name = "Add rule" }).ClickAsync();
        var rule = localEditor.Locator(":scope > section").First;
        var actionType = rule.Locator("label").Filter(new() { HasText = "Action type" }).Locator("select").First;
        await Assertions.Expect(actionType.Locator("option[value='player.kick']")).ToHaveCountAsync(1);
        await Assertions.Expect(actionType.Locator("option[value='message.public']")).ToHaveCountAsync(0);
        await Assertions.Expect(localEditor).Not.ToContainTextAsync("New conditions and actions require verified server compatibility");
        var inherited = page.Locator("#VpnProtection-rule-overrides [data-rules-editor]");
        await Assertions.Expect(inherited).ToContainTextAsync("global-public-message");
        await Assertions.Expect(inherited).ToContainTextAsync("Inherited action message.public: unsupported for this server.");
        await Assertions.Expect(inherited).ToContainTextAsync("An unsupported action is skipped.");
        browser.AssertNoBrowserErrors();
    }

    private static ConfigurationDto Configuration(string json)
        => JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = VpnProtectionSettingsConstants.Namespace,
            Configuration = json,
            LastModifiedUtc = DateTime.UtcNow
        }))!;
}
