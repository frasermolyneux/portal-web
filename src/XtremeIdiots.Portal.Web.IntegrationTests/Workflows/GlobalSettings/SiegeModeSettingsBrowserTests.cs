using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GlobalSettings;

[Trait("Category", "Browser")]
public sealed class SiegeModeSettingsBrowserTests
{
    [Fact]
    public async Task WelcomeEditor_PreservesTemplateAndSavesTiming()
    {
        var scenario = new SettingsScenario();
        scenario.SetConfiguration(WelcomeRulesSettingsConstants.Namespace, /*lang=json,strict*/ """
            {"schemaVersion":1,"featureId":"welcome-messages","enabled":true,
             "timing":{"connectionDelaySeconds":5,"freshnessSeconds":120},
             "rules":[{"id":"global","enabled":true,"order":0,"condition":{"conditionId":"always","parameters":{}},
             "actions":[{"id":"say","typeId":"message.public","parameters":{"message":"Hello {context.player.Name}"},
             "templateParameters":["message"],"dependsOnActionIds":[]}],"afterMatch":"StopProcessingRules"}]}
            """);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/GlobalSettings").AbsoluteUri);
        await page.Locator("#welcome-messages-tab-btn").ClickAsync();
        var editor = page.Locator("#global-welcome-rules");
        await Assertions.Expect(editor.Locator("[data-action-preview='message']")).ToContainTextAsync("Hello ExamplePlayer");
        await page.Locator("#WelcomeRules_ConnectionDelaySeconds").FillAsync("8");
        await SaveSettings(page);
        using var saved = JsonDocument.Parse(scenario.WelcomeJson!);
        Assert.Equal(8, saved.RootElement.GetProperty("timing").GetProperty("connectionDelaySeconds").GetInt32());
        Assert.Equal("message", saved.RootElement.GetProperty("rules")[0].GetProperty("actions")[0].GetProperty("templateParameters")[0].GetString());
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task SiegeEditor_MissingEnabledFieldDoesNotOverwriteSettings()
    {
        var scenario = new SettingsScenario();
        const string existing = /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"siege-mode","enabled":true,"durationMinutes":180,"rules":[]}""";
        scenario.SetConfiguration(SiegeModeSettingsConstants.Namespace, existing);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);
        await page.Locator("[name='SiegeMode.Enabled']").EvaluateAllAsync<int>("inputs => { inputs.forEach(input => input.remove()); return inputs.length; }");

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save Settings", Exact = true }).ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath == "/GlobalSettings");
        Assert.Equal(200, response.Status);
        Assert.Equal(existing, scenario.SiegeJson);
        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("[data-valmsg-for='SiegeMode.Enabled']")).Not.ToBeEmptyAsync();
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task SiegeEditor_RoundTripsNestedGroupsOrderedActionsAndMultipleRules()
    {
        var scenario = new SettingsScenario();
        scenario.SetConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "siege-mode",
              "enabled": true,
              "durationMinutes": 180,
              "rules": [
                {
                  "id": "nested-rule",
                  "enabled": true,
                  "order": 0,
                  "condition": {
                    "groupOperator": "And",
                    "conditions": [
                      {
                        "conditionId": "player.first-seen-ip",
                        "parameters": { "operator": "IsNew", "referenceScope": "SiegeBaseline" }
                      },
                      {
                        "groupOperator": "Or",
                        "conditions": [
                          {
                            "conditionId": "vpn.intelligence",
                            "parameters": { "signal": "ProxyCheckIsVpn", "operator": "Equal", "expectedValue": "true" }
                          },
                          {
                            "conditionId": "vpn.intelligence",
                            "parameters": { "signal": "MaxMindIsAnonymousVpn", "operator": "Equal", "expectedValue": "true" }
                          },
                          {
                            "conditionId": "player.tags",
                            "parameters": {
                              "tags": ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"],
                              "operator": "ContainsAny"
                            }
                          }
                        ]
                      }
                    ]
                  },
                  "actions": [
                    { "id": "observe", "typeId": "moderation.observation", "parameters": { "reason": "observe" }, "dependsOnActionIds": [] },
                    { "id": "kick", "typeId": "player.kick", "parameters": { "reason": "kick" }, "dependsOnActionIds": [] }
                  ],
                  "afterMatch": "StopProcessingRules"
                },
                {
                  "id": "fallback",
                  "enabled": true,
                  "order": 1,
                  "condition": {
                    "conditionId": "player.first-seen-guid",
                    "parameters": { "operator": "IsNew", "referenceScope": "SiegeBaseline" }
                  },
                  "actions": [
                    { "id": "fallback-kick", "typeId": "player.kick", "parameters": { "reason": "fallback" }, "dependsOnActionIds": [] }
                  ],
                  "afterMatch": "ContinueToNextRule"
                }
              ]
            }
            """);

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);

        var rules = page.Locator("#global-siege-rules [data-rules-editor] > section");
        await Assertions.Expect(rules).ToHaveCountAsync(2);
        var firstRule = rules.Nth(0);
        await Assertions.Expect(firstRule.GetByRole(AriaRole.Heading, new() { Name = "WHEN" })).ToBeVisibleAsync();
        await Assertions.Expect(firstRule.GetByRole(AriaRole.Heading, new() { Name = "THEN" })).ToBeVisibleAsync();
        await Assertions.Expect(firstRule).ToContainTextAsync("Any condition can match");
        await Assertions.Expect(firstRule).ToContainTextAsync("All conditions must match");
        var observation = firstRule.GetByRole(AriaRole.Heading, new() { Name = "Action 1", Exact = true }).Locator("..");
        var kick = firstRule.GetByRole(AriaRole.Heading, new() { Name = "Action 2", Exact = true }).Locator("..");
        await Assertions.Expect(observation.GetByText("Post to forums")).ToBeVisibleAsync();
        await Assertions.Expect(kick.GetByText("Post to forums")).ToHaveCountAsync(0);
        await Assertions.Expect(observation.GetByText("Action ID: observe")).Not.ToBeVisibleAsync();
        await observation.GetByText("Advanced action settings").ClickAsync();
        await Assertions.Expect(observation.GetByText("Action ID: observe")).ToBeVisibleAsync();
        await kick.GetByText("Advanced action settings").ClickAsync();
        await Assertions.Expect(kick.Locator("option[value='observe']")).ToHaveTextAsync("Action 1: Observation");
        await firstRule.GetByRole(AriaRole.Button, new() { Name = "Move action up" }).Nth(1).ClickAsync();
        await Assertions.Expect(firstRule.Locator("h6").Filter(new() { HasText = "Action 1" })).ToBeVisibleAsync();

        await SaveSettings(page);

        using var saved = JsonDocument.Parse(scenario.SiegeJson!);
        var savedRules = saved.RootElement.GetProperty("rules");
        Assert.Equal(2, savedRules.GetArrayLength());
        Assert.Equal("nested-rule", savedRules[0].GetProperty("id").GetString());
        Assert.Equal("fallback", savedRules[1].GetProperty("id").GetString());
        var actions = savedRules[0].GetProperty("actions");
        Assert.Equal("kick", actions[0].GetProperty("id").GetString());
        Assert.Equal("observe", actions[1].GetProperty("id").GetString());
        var conditions = savedRules[0].GetProperty("condition").GetProperty("conditions");
        Assert.Equal("Or", conditions[1].GetProperty("groupOperator").GetString());
        Assert.Equal(3, conditions[1].GetProperty("conditions").GetArrayLength());
        var tags = conditions[1].GetProperty("conditions")[2].GetProperty("parameters").GetProperty("tags");
        Assert.Equal(2, tags.GetArrayLength());
        Assert.Equal("11111111-1111-1111-1111-111111111111", tags[0].GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", tags[1].GetString());

        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#global-siege-rules [data-rules-editor] > section")).ToHaveCountAsync(2);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task VpnEditor_ChangingSignalResetsValues_AndPreservesOtherRules()
    {
        var scenario = new SettingsScenario();
        scenario.SetConfiguration(VpnProtectionSettingsConstants.Namespace, /*lang=json,strict*/ """
            {
              "schemaVersion": 1,
              "featureId": "vpn-protection",
              "enabled": true,
              "rules": [
                {
                  "id": "risk",
                  "enabled": true,
                  "order": 0,
                  "condition": {
                    "conditionId": "vpn.intelligence",
                    "parameters": { "signal": "ProxyCheckRiskScore", "operator": "GreaterThan", "expectedValue": "75" }
                  },
                  "actions": [
                    { "id": "observe-risk", "typeId": "moderation.observation", "parameters": { "reason": "risk" }, "dependsOnActionIds": [] }
                  ],
                  "afterMatch": "StopProcessingRules"
                },
                {
                  "id": "vpn",
                  "enabled": true,
                  "order": 1,
                  "condition": {
                    "conditionId": "vpn.intelligence",
                    "parameters": { "signal": "ProxyCheckIsVpn", "operator": "Equal", "expectedValue": "true" }
                  },
                  "actions": [
                    { "id": "observe-vpn", "typeId": "moderation.observation", "parameters": { "reason": "vpn" }, "dependsOnActionIds": [] }
                  ],
                  "afterMatch": "ContinueToNextRule"
                }
              ]
            }
            """);

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);
        await page.Locator("#vpn-protection-tab-btn").ClickAsync();

        var rules = page.Locator("#global-vpn-rules [data-rules-editor] > section");
        await Assertions.Expect(rules).ToHaveCountAsync(2);
        var condition = rules.Nth(0).Locator("[data-rules-editor], .border.rounded.p-3").First;
        var signal = condition.Locator("label").Filter(new() { HasText = "Signal" }).Locator("select");
        await signal.SelectOptionAsync("MaxMindStatus");
        var comparison = condition.Locator("label").Filter(new() { HasText = "Operator" }).Locator("select");
        var expectedValue = condition.Locator("label").Filter(new() { HasText = "Expected Value" }).Locator("select, input");
        await Assertions.Expect(comparison).ToHaveValueAsync("");
        await Assertions.Expect(expectedValue).ToHaveValueAsync("");
        await comparison.SelectOptionAsync("Equal");
        await expectedValue.SelectOptionAsync("Success");

        await SaveSettings(page);

        using var saved = JsonDocument.Parse(scenario.VpnJson!);
        var savedRules = saved.RootElement.GetProperty("rules");
        Assert.Equal(2, savedRules.GetArrayLength());
        var firstCondition = savedRules[0].GetProperty("condition").GetProperty("parameters");
        Assert.Equal("MaxMindStatus", firstCondition.GetProperty("signal").GetString());
        Assert.Equal("Equal", firstCondition.GetProperty("operator").GetString());
        Assert.Equal("Success", firstCondition.GetProperty("expectedValue").GetString());
        Assert.Equal("vpn", savedRules[1].GetProperty("id").GetString());
        Assert.Equal("ProxyCheckIsVpn", savedRules[1].GetProperty("condition").GetProperty("parameters").GetProperty("signal").GetString());
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task UnknownParameters_ShowGameSupportAndRemainPreserved()
    {
        var scenario = new SettingsScenario();
        scenario.SetConfiguration(SiegeModeSettingsConstants.Namespace, /*lang=json,strict*/ $$"""
            {
              "schemaVersion": 1,
              "featureId": "siege-mode",
              "enabled": true,
              "durationMinutes": 180,
              "rules": [{
                "id": "preserved-rule",
                "enabled": true,
                "order": 0,
                "condition": {
                  "conditionId": "vpn.intelligence",
                  "parameters": {
                    "signal": "ProxyCheckIsVpn",
                    "operator": "Equal",
                    "expectedValue": "true",
                    "legacySignalMetadata": "keep-me"
                  }
                },
                "actions": [{
                  "id": "kick",
                  "typeId": "player.kick",
                  "parameters": { "reason": "legacy action", "legacyActionMetadata": "keep-action" },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "StopProcessingRules"
              }, {
                "id": "future-rule",
                "enabled": true,
                "order": 1,
                "condition": {
                  "conditionId": "future.condition",
                  "parameters": { "futureValue": "keep-condition" }
                },
                "actions": [{
                  "id": "future-action",
                  "typeId": "future.action",
                  "parameters": { "futureValue": "keep-future-action" },
                  "dependsOnActionIds": []
                }],
                "afterMatch": "ContinueToNextRule"
              }]
            }
            """);

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await OpenSettings(page, browser.Host.BaseAddress);

        var rule = page.Locator("#global-siege-rules [data-rules-editor] > section").First;
        await Assertions.Expect(rule.GetByText("Supported games:")).ToHaveCountAsync(2);
        await Assertions.Expect(rule.Locator("img[src$='CallOfDuty4x.png']")).ToHaveCountAsync(2);
        await Assertions.Expect(rule.Locator("img[src$='CallOfDuty4x.png']").First).ToBeVisibleAsync();
        await Assertions.Expect(rule.Locator("img[src$='CallOfDuty4x.png']").First).ToHaveAttributeAsync("aria-label", "Call of Duty 4x");
        await Assertions.Expect(rule).ToContainTextAsync("Call of Duty 4x");
        await Assertions.Expect(rule).ToContainTextAsync("Additional preserved parameters");
        var futureRule = page.Locator("#global-siege-rules [data-rules-editor] > section").Nth(1);
        await Assertions.Expect(futureRule).ToContainTextAsync("Unsupported condition (preserved)");
        await Assertions.Expect(futureRule).ToContainTextAsync("Unsupported action (preserved)");
        await SaveSettings(page);

        using var saved = JsonDocument.Parse(scenario.SiegeJson!);
        var savedRule = saved.RootElement.GetProperty("rules")[0];
        Assert.Equal("keep-me", savedRule.GetProperty("condition").GetProperty("parameters").GetProperty("legacySignalMetadata").GetString());
        Assert.Equal("keep-action", savedRule.GetProperty("actions")[0].GetProperty("parameters").GetProperty("legacyActionMetadata").GetString());
        var future = saved.RootElement.GetProperty("rules")[1];
        Assert.Equal("future.condition", future.GetProperty("condition").GetProperty("conditionId").GetString());
        Assert.Equal("keep-condition", future.GetProperty("condition").GetProperty("parameters").GetProperty("futureValue").GetString());
        Assert.Equal("future.action", future.GetProperty("actions")[0].GetProperty("typeId").GetString());
        Assert.Equal("keep-future-action", future.GetProperty("actions")[0].GetProperty("parameters").GetProperty("futureValue").GetString());
        browser.AssertNoBrowserErrors();
    }

    private async static Task OpenSettings(IPage page, Uri baseAddress)
    {
        await page.GotoAsync(new Uri(baseAddress, "/GlobalSettings").AbsoluteUri);
        await page.Locator("#siege-mode-tab-btn").ClickAsync();
        await Assertions.Expect(page.Locator("#siege-mode-tab")).ToBeVisibleAsync();
    }

    private async static Task SaveSettings(IPage page)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Save Settings", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/GlobalSettings");
        Assert.Equal(302, response.Status);
        await Assertions.Expect(page.Locator("#globalSettingsTabs")).ToBeVisibleAsync();
    }

    private sealed class SettingsScenario
    {
        private readonly ConcurrentDictionary<string, string> configurations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Mock<IRepositoryApiClient> repository = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };

        public SettingsScenario()
        {
            Mock.Get(repository.Object.GlobalConfigurations.V1)
                .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ApiResult<CollectionModel<ConfigurationDto>>(
                    HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(
                        new CollectionModel<ConfigurationDto>([.. configurations.Select(entry => CreateConfiguration(entry.Key, entry.Value))]))));
            Mock.Get(repository.Object.GlobalConfigurations.V1)
                .Setup(api => api.UpsertConfiguration(It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpsertConfigurationDto, CancellationToken>((ns, dto, _) => configurations[ns] = dto.Configuration)
                .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
            Mock.Get(repository.Object.Tags.V1)
                .Setup(api => api.GetTags(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(
                    HttpStatusCode.OK, new ApiResponse<CollectionModel<TagDto>>(
                        new CollectionModel<TagDto>([
                            new TagDto { TagId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "SeniorAdmin" },
                            new TagDto { TagId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "GameAdmin" }
                        ]))));
        }

        public string? SiegeJson => configurations.GetValueOrDefault(SiegeModeSettingsConstants.Namespace);
        public string? VpnJson => configurations.GetValueOrDefault(VpnProtectionSettingsConstants.Namespace);
        public string? WelcomeJson => configurations.GetValueOrDefault(WelcomeRulesSettingsConstants.Namespace);

        public void SetConfiguration(string ns, string configuration) => configurations[ns] = configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
        }

        private static ConfigurationDto CreateConfiguration(string ns, string configuration)
        {
            return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
            {
                Namespace = ns,
                Configuration = configuration,
                LastModifiedUtc = DateTime.UtcNow
            }))!;
        }
    }
}
