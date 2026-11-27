using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Trait("Category", "Browser")]
public sealed class WelcomeRulesEditorBrowserTests
{
    [Fact]
    public async Task ServerFeatures_NavigationKeepsExistingEditorsAndClarifiesBroadcasts()
    {
        var scenario = new FileTransportScenario();
        var serverJson = Newtonsoft.Json.Linq.JObject.FromObject(scenario.GameServer);
        serverJson["AgentEnabled"] = true;
        serverJson["RconEnabled"] = true;
        var server = serverJson.ToObject<GameServerDto>()!;
        Mock.Get(scenario.RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/GameServers/Edit/{scenario.GameServerId}").AbsoluteUri);
        var tabs = page.Locator("#serverConfigTabs");
        await Assertions.Expect(tabs).ToContainTextAsync("Messages");
        await Assertions.Expect(tabs).ToContainTextAsync("Access Protection");
        await Assertions.Expect(tabs).ToContainTextAsync("Server Operations");
        await tabs.GetByRole(AriaRole.Tab, new() { Name = "Broadcast Messages" }).ClickAsync();
        await Assertions.Expect(page.Locator("#broadcasts-tab")).ToContainTextAsync("agent inheritance is not currently supported");
        await Assertions.Expect(page.Locator("#broadcasts-tab")).ToContainTextAsync("removes this server's broadcast configuration");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Editor_DisclosuresPreserveStateAndRevealInvalidFields()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/js/cod-colors.js").AbsoluteUri);
        await page.SetContentAsync("""
            <form>
              <div id="first" data-rules-catalogue-editor data-feature-id="welcome-messages"
                   data-override="false" data-server-context="true">
                <input type="hidden" data-rules-value><div data-rules-editor></div>
              </div>
              <div id="second" data-rules-catalogue-editor data-feature-id="welcome-messages"
                   data-override="false" data-server-context="true">
                <input type="hidden" data-rules-value><div data-rules-editor></div>
              </div>
              <button type="submit">Save</button>
            </form>
            """);
        await page.EvaluateAsync("""
            () => {
                for (const root of document.querySelectorAll('[data-rules-catalogue-editor]')) {
                    root.dataset.catalogue = JSON.stringify({
                        features: [{ featureId: 'welcome-messages', conditionIds: ['tagged'], actionTypeIds: ['message.private'] }],
                        conditions: [{ conditionId: 'tagged', displayName: 'Tagged player', parameters: [
                            { name: 'tags', type: 'string[]', allowedValues: [] },
                            { name: 'operator', type: 'string', allowedValues: ['Any', 'All'] }
                        ] }],
                        actions: [{ actionTypeId: 'message.private', displayName: 'Private message',
                            parameters: [{ name: 'message', type: 'string', allowedValues: [], supportsTemplates: true, required: true }] }]
                    });
                    root.dataset.tags = JSON.stringify([{ value: 'tag-a', text: 'Admin' }]);
                    root.dataset.compatibility = JSON.stringify({ tagged: true, 'message.private': true });
                    root.dataset.globalRules = '[]';
                    root.querySelector('[data-rules-value]').value = JSON.stringify([{
                        id: 'existing', enabled: false, order: 0,
                        condition: { conditionId: 'tagged', parameters: { tags: ['tag-a'], operator: 'Any' } },
                        actions: [
                            { id: 'message', typeId: 'message.private', parameters: { message: 'Hello player' }, dependsOnActionIds: [] },
                            { id: 'old', typeId: 'old.unknown', parameters: {}, dependsOnActionIds: [] }
                        ],
                        afterMatch: 'StopProcessingRules'
                    }]);
                }
            }
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/rules-catalogue-editor.js").AbsoluteUri });
        var editor = page.Locator("#first");
        var other = page.Locator("#second");
        var rule = editor.Locator("[data-rule-disclosure='rule']").First;
        var condition = editor.Locator("[data-rule-disclosure='condition']").First;
        var action = editor.Locator("[data-rule-disclosure='action']").First;
        var original = await editor.Locator("[data-rules-value]").InputValueAsync();
        await Assertions.Expect(rule).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(rule.Locator(":scope > summary")).ToContainTextAsync("existing | Disabled | 1 conditions, 2 actions");
        await Assertions.Expect(rule.Locator(":scope > summary")).ToContainTextAsync("Needs attention");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await Assertions.Expect(other.Locator("[data-rule-disclosure='rule']")).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(condition.Locator(":scope > summary")).ToContainTextAsync("Tags: Admin; Operator: Any");
        await Assertions.Expect(action.Locator(":scope > summary")).ToContainTextAsync("Message: Hello player");
        await editor.GetByLabel("Message", new() { Exact = true }).FillAsync("Updated message");
        await Assertions.Expect(action.Locator(":scope > summary")).ToContainTextAsync("Updated message");
        await condition.Locator(":scope > summary").ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Add action" }).ClickAsync();
        await Assertions.Expect(condition).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(editor.Locator("[data-rule-disclosure='action']").Last).ToHaveAttributeAsync("open", "");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Remove action" }).Last.ClickAsync();
        await editor.GetByLabel("Message", new() { Exact = true }).FillAsync("Hello player");
        Assert.Equal(original, await editor.Locator("[data-rules-value]").InputValueAsync());
        await editor.GetByRole(AriaRole.Button, new() { Name = "Add rule", Exact = true }).ClickAsync();
        var newRule = editor.Locator("[data-rule-disclosure='rule']").Last;
        await Assertions.Expect(newRule).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(newRule.Locator("[data-rule-disclosure='condition']")).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(newRule.Locator("[data-rule-disclosure='action']")).ToHaveAttributeAsync("open", "");
        await newRule.GetByRole(AriaRole.Button, new() { Name = "Remove rule" }).ClickAsync();
        Assert.Equal(original, await editor.Locator("[data-rules-value]").InputValueAsync());
        await editor.GetByLabel("Message", new() { Exact = true }).FillAsync("");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Collapse all" }).ClickAsync();
        await page.EvaluateAsync("document.querySelector('form').requestSubmit()");
        await Assertions.Expect(rule).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(action).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(editor.GetByLabel("Message", new() { Exact = true })).ToBeFocusedAsync();
        await editor.GetByLabel("Message", new() { Exact = true }).FillAsync("Hello player");
        await editor.GetByLabel("Rule ID").FillAsync("");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Collapse all" }).ClickAsync();
        await Assertions.Expect(rule.Locator(":scope > summary")).ToContainTextAsync("Invalid fields");
        await page.EvaluateAsync("document.querySelector('form').requestSubmit()");
        await Assertions.Expect(rule).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(editor.GetByLabel("Rule ID")).ToBeFocusedAsync();
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task ServerEditor_PreviewsAndSavesWelcomeRulesWithoutAgent()
    {
        var scenario = new FileTransportScenario();
        static ConfigurationDto Configuration(string json) => JsonConvert.DeserializeObject<ConfigurationDto>(
            JsonConvert.SerializeObject(new { Namespace = WelcomeRulesSettingsConstants.Namespace, Configuration = json }))!;
        var global = Configuration(/*lang=json,strict*/ """{"schemaVersion":1,"featureId":"welcome-messages","enabled":true,"timing":{"connectionDelaySeconds":5,"freshnessSeconds":120},"rules":[]}""");
        var server = Configuration(/*lang=json,strict*/ """
            {"schemaVersion":1,"featureId":"welcome-messages","enabledOverride":"Enabled","inheritGlobalRules":true,
             "rules":[{"id":"local","enabled":true,"order":0,"condition":{"conditionId":"always","parameters":{}},
             "actions":[{"id":"message","typeId":"message.private","parameters":{"message":"Hello {context.player.Name}"},
             "templateParameters":["message"],"dependsOnActionIds":[]}],"afterMatch":"StopProcessingRules"}],"ruleOverrides":[]}
            """);
        Mock.Get(scenario.RepositoryClient.Object.GlobalConfigurations.V1)
            .Setup(api => api.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([global]))));
        Mock.Get(scenario.RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([scenario.SftpConfiguration, server]))));
        var servers = new Mock<IServersApiClient> { DefaultValue = DefaultValue.Mock };
        var budget = new RuleMessageChannelBudgetDto
        {
            Status = RuleMessageBudgetStatus.Available,
            Budget = new MessageChannelBudget(RuleMessageChannels.RconPrivateChat, 149, "rcon", "Synthetic evidence", 32, "CallOfDuty4"),
            Constraints = new RconMessageCharacterConstraintsDto { AsciiOnly = true },
            Detail = "Synthetic evidence"
        };
        servers.Setup(client => client.RulesNativeAction.V1.GetMessageChannelBudget(
                scenario.GameServerId, RuleMessageChannels.RconPrivateChat, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleMessageChannelBudgetDto>(HttpStatusCode.OK, new ApiResponse<RuleMessageChannelBudgetDto>(budget)));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            scenario.ConfigureServices(services);
            services.RemoveAll<IServersApiClient>();
            services.AddSingleton(servers.Object);
            services.RemoveAll<IGameServerSettingsService>();
            services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        });
        var page = browser.Page;
        var path = $"/GameServers/Edit/{scenario.GameServerId}";
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, path).AbsoluteUri);
        await page.Locator("#welcome-messages-tab-btn").ClickAsync();
        var editor = page.Locator("#WelcomeRules-local-rules");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await Assertions.Expect(editor).ToContainTextAsync("Effective at page load: 5 second delay; 120 second freshness");
        var preview = editor.Locator("[data-action-preview='message']");
        await Assertions.Expect(preview).ToContainTextAsync("Hello ExamplePlayer");
        await Assertions.Expect(preview).ToContainTextAsync("RCON preview for CallOfDuty4: 149 total bytes, including 32 reserved prefix bytes.");
        await Assertions.Expect(preview).ToContainTextAsync("another provider");
        await preview.GetByLabel("Message preview data").SelectOptionAsync("missing");
        await Assertions.Expect(preview).ToContainTextAsync("Hello player");
        await editor.GetByLabel("Rule ID").FillAsync("");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Collapse all" }).ClickAsync();
        await page.Locator("#general-tab-btn").ClickAsync();
        await page.GetByTestId("game-server-save").ClickAsync();
        await Assertions.Expect(page.Locator("#welcome-messages-tab-btn")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await Assertions.Expect(editor.Locator("[data-rule-disclosure='rule']")).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(editor.GetByLabel("Rule ID")).ToBeFocusedAsync();
        Assert.Empty(scenario.UpsertedConfigurations);
        await editor.GetByLabel("Rule ID").FillAsync("local");
        await editor.GetByLabel("Connection delay override (seconds)").FillAsync("61");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Collapse all" }).ClickAsync();
        await page.Locator("#general-tab-btn").ClickAsync();
        await page.GetByTestId("game-server-save").ClickAsync();
        await Assertions.Expect(page.Locator("#welcome-messages-tab-btn")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await Assertions.Expect(editor.Locator("[data-rule-disclosure='rule']")).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(editor.GetByLabel("Connection delay override (seconds)")).ToBeFocusedAsync();
        await editor.GetByLabel("Connection delay override (seconds)").FillAsync("");
        await page.Locator("#WelcomeRules_FreshnessSeconds").FillAsync("4");
        var invalidResponse = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("game-server-save").ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(200, invalidResponse.Status);
        Assert.Empty(scenario.UpdatedGameServers);
        Assert.Empty(scenario.UpsertedConfigurations);
        await Assertions.Expect(page.Locator("[data-valmsg-summary]")).ToContainTextAsync("delivery window");
        await page.Locator("#welcome-messages-tab-btn").ClickAsync();
        await page.Locator("#WelcomeRules_FreshnessSeconds").FillAsync("");
        await page.Locator("#WelcomeRules_ConnectionDelaySeconds").FillAsync("12");
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("game-server-save").ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(302, response.Status);
        var saved = Assert.Single(scenario.UpsertedConfigurations, entry => entry.Namespace == WelcomeRulesSettingsConstants.Namespace);
        using var document = JsonDocument.Parse(saved.Configuration);
        Assert.Equal(12, document.RootElement.GetProperty("timing").GetProperty("connectionDelaySeconds").GetInt32());
        Assert.Equal("Hello {context.player.Name}", document.RootElement.GetProperty("rules")[0].GetProperty("actions")[0].GetProperty("parameters").GetProperty("message").GetString());
        Assert.DoesNotContain(scenario.UpsertedConfigurations, entry => entry.Namespace == "welcomeMessages");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task ServerEditor_RejectsOverridesOfClientSuppliedGlobalRules()
    {
        var scenario = new FileTransportScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, services =>
        {
            scenario.ConfigureServices(services);
            services.RemoveAll<IGameServerSettingsService>();
            services.AddScoped<IGameServerSettingsService, GameServerSettingsService>();
        });
        var page = browser.Page;
        var path = $"/GameServers/Edit/{scenario.GameServerId}";
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, path).AbsoluteUri);
        await page.EvaluateAsync("""
            () => {
                const form = document.querySelector('input[name="WelcomeRules.RuleOverridesJson"]').form;
                form.addEventListener('submit', () => {
                    form.querySelector('input[name="WelcomeRules.RuleOverridesJson"]').value =
                        JSON.stringify([{ ruleId: 'forged' }]);
                });
                const field = document.createElement('input');
                field.type = 'hidden';
                field.name = 'GlobalWelcomeRules.RulesJson';
                field.value = JSON.stringify([{
                    id: 'forged', enabled: true, order: 0,
                    condition: { conditionId: 'always', parameters: {} },
                    actions: [{ id: 'message', typeId: 'message.private', parameters: { message: 'hello' }, dependsOnActionIds: [] }],
                    afterMatch: 'StopProcessingRules'
                }]);
                form.append(field);
            }
            """);

        var response = await page.RunAndWaitForResponseAsync(
            () => page.GetByTestId("game-server-save").ClickAsync(),
            result => result.Request.Method == "POST" && new Uri(result.Url).AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(200, response.Status);
        await Assertions.Expect(page.Locator("[data-valmsg-summary]")).ToContainTextAsync("global Welcome Rules rule that is no longer available");
        Assert.Empty(scenario.UpdatedGameServers);
        Assert.Empty(scenario.UpsertedConfigurations);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Editor_DoesNotReplaceNewPreviewWithOlderResponse()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var page = browser.Page;
        await page.SetContentAsync("""
            <form>
              <div id="editor" data-rules-catalogue-editor data-feature-id="welcome-messages"
                   data-override="false" data-server-context="false" data-show-inherited-rules="false"
                   data-preview-url="/preview">
                <input type="hidden" data-rules-value>
                <div data-rules-editor></div>
              </div>
            </form>
            """);
        await page.EvaluateAsync("""
            () => {
                window.previewRequests = [];
                window.fetch = (_url, options) => new Promise(resolve => {
                    window.previewRequests.push({
                        message: JSON.parse(options.body).parameters.message,
                        complete: text => resolve({ ok: true, json: async () => ({
                            errors: [], diagnostics: [], warnings: [], fragments: [], text
                        }) })
                    });
                });
                const root = document.querySelector('#editor');
                root.dataset.catalogue = JSON.stringify({
                    features: [{ featureId: 'welcome-messages', conditionIds: ['always'], actionTypeIds: ['message.public'] }],
                    conditions: [{ conditionId: 'always', displayName: 'Everyone', parameters: [] }],
                    actions: [{ actionTypeId: 'message.public', displayName: 'Public message',
                        parameters: [{ name: 'message', type: 'string', allowedValues: [],
                            supportsTemplates: true, supportsMessageList: true }] }]
                });
                root.dataset.tags = '[]';
                root.dataset.compatibility = '{}';
                root.dataset.globalRules = '[]';
                root.querySelector('[data-rules-value]').value = JSON.stringify([{
                    id: 'welcome', enabled: true, order: 0,
                    condition: { conditionId: 'always', parameters: {} },
                    actions: [{ id: 'say', typeId: 'message.public', dependsOnActionIds: [],
                        parameters: { message: 'old' } }],
                    afterMatch: 'StopProcessingRules'
                }]);
            }
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/rules-catalogue-editor.js").AbsoluteUri });
        await page.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await page.WaitForFunctionAsync("window.previewRequests.length === 1");
        await page.GetByLabel("Message", new() { Exact = true }).FillAsync("new");
        await page.WaitForFunctionAsync("window.previewRequests.length === 2");
        await page.EvaluateAsync("window.previewRequests[1].complete('new preview')");
        var preview = page.Locator("[data-action-preview='message']");
        await Assertions.Expect(preview).ToContainTextAsync("new preview");
        await page.EvaluateAsync("""
            async () => {
                window.previewRequests[0].complete('old preview');
                await Promise.resolve();
                await Promise.resolve();
                await Promise.resolve();
            }
            """);
        await Assertions.Expect(preview).ToContainTextAsync("new preview");
        await Assertions.Expect(preview).Not.ToContainTextAsync("old preview");
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Editor_PreviewsFragmentsSafely_AndShowsFallbacksAndErrors(bool useCodColorCodes)
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/js/cod-colors.js").AbsoluteUri);
        await page.RouteAsync("**/test-rules-preview", route =>
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var mode = request.RootElement.GetProperty("mode").GetString();
            var message = request.RootElement.GetProperty("parameters").GetProperty("message").GetString();
            var body = message == "bad"
                ? /*lang=json,strict*/ """{"errors":["Unknown token"],"diagnostics":[],"warnings":[],"fragments":[]}"""
                : mode == "missing"
                    ? /*lang=json,strict*/ """{"errors":[],"diagnostics":["Fallback used"],"warnings":[],"fragments":[{"itemIndex":0,"fragmentIndex":0,"text":"player","utf8ByteCount":6,"totalPayloadByteCount":6}]}"""
                    : /*lang=json,strict*/ """{"errors":[],"diagnostics":[],"warnings":["Budget includes prefix"],"fragments":[{"itemIndex":0,"fragmentIndex":0,"text":"^1<img src=x>","utf8ByteCount":13,"totalPayloadByteCount":16}]}""";
            body = body.Replace("\"fragments\":", $"\"useCodColorCodes\":{useCodColorCodes.ToString().ToLowerInvariant()},\"fragments\":", StringComparison.Ordinal);
            return route.FulfillAsync(new() { ContentType = "application/json", Body = body });
        });
        await page.SetContentAsync("""
            <form>
              <input name="__RequestVerificationToken" value="test">
              <div id="editor" data-rules-catalogue-editor data-feature-id="welcome-messages"
                   data-override="false" data-server-context="false" data-show-inherited-rules="false">
                <input type="hidden" data-rules-value>
                <div data-rules-editor></div>
              </div>
            </form>
            """);
        await page.EvaluateAsync("""
            url => {
                const root = document.querySelector('#editor');
                root.dataset.previewUrl = url;
                root.dataset.catalogue = JSON.stringify({
                    features: [{ featureId: 'welcome-messages', conditionIds: ['always'], actionTypeIds: ['message.public'] }],
                    conditions: [{ conditionId: 'always', displayName: 'Everyone', parameters: [] }],
                    actions: [{ actionTypeId: 'message.public', displayName: 'Public message',
                        parameters: [{ name: 'message', type: 'string', allowedValues: [],
                            supportsTemplates: true, supportsMessageList: true }] }]
                });
                root.dataset.tags = '[]';
                root.dataset.compatibility = '{}';
                root.dataset.globalRules = '[]';
                root.querySelector('[data-rules-value]').value = JSON.stringify([{
                    id: 'welcome', enabled: true, order: 0,
                    condition: { conditionId: 'always', parameters: {} },
                    actions: [{ id: 'say', typeId: 'message.public', dependsOnActionIds: [],
                        parameters: { message: 'hello' }, templateParameters: ['message'] }],
                    afterMatch: 'StopProcessingRules'
                }]);
            }
            """, new Uri(browser.Host.BaseAddress, "/test-rules-preview").AbsoluteUri);
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/cod-colors.js").AbsoluteUri });
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/rules-catalogue-editor.js").AbsoluteUri });
        await page.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();

        var preview = page.Locator("[data-action-preview='message']");
        await Assertions.Expect(preview).ToContainTextAsync("13 UTF-8 bytes (16 including reserved prefix)");
        await Assertions.Expect(preview).ToContainTextAsync("Budget includes prefix");
        if (useCodColorCodes)
            await Assertions.Expect(preview.Locator(".cod-color-1")).ToHaveTextAsync("<img src=x>");
        else
        {
            await Assertions.Expect(preview.Locator(".cod-message-preview")).ToHaveTextAsync("^1<img src=x>");
            await Assertions.Expect(preview.Locator(".cod-color-1")).ToHaveCountAsync(0);
        }

        await Assertions.Expect(preview.Locator("img")).ToHaveCountAsync(0);
        await preview.GetByLabel("Message preview data").SelectOptionAsync("missing");
        await Assertions.Expect(preview).ToContainTextAsync("Fallback used");
        await Assertions.Expect(preview.Locator(".cod-message-preview")).ToHaveTextAsync("player");
        await page.GetByLabel("Message", new() { Exact = true }).FillAsync("bad");
        await Assertions.Expect(preview).ToContainTextAsync("Unknown token");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task RulesAudit_ShowsDiagnosticsAcrossFeaturesWithoutRenderingHtml()
    {
        var scenario = new FileTransportScenario();
        var waitingId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var noMatchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var waiting = new RuleExecutionAuditDto
        {
            ExecutionId = waitingId,
            GameServerId = scenario.GameServerId,
            FeatureId = "welcome-messages",
            State = RuleFeatureExecutionState.Waiting,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Username = "<img src=x>",
            Coordination = new RuleExecutionAuditCoordinationDto
            {
                DueAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(2),
                WaitingReason = "Waiting for protection evidence"
            },
            Diagnostics =
            [
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.WaitingForProtection, Detail = "<b>Protection pending</b>" },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.FallbackUsed, TokenKey = "context.player.Country", Source = "GeoLocation", Detail = "Fallback selected" },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.DiagnosticsOmitted, OmittedCount = 7 }
            ],
            Actions =
            [
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome",
                    ActionId = "greet",
                    Outcome = RuleActionExecutionOutcome.OutcomeUnknown,
                    Detail = "Fragment result unknown",
                    MessageIndex = 0,
                    FragmentIndex = 1
                },
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome",
                    ActionId = "dispatch",
                    Outcome = RuleActionExecutionOutcome.Dispatched,
                    UpdatedAtUtc = now
                },
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome",
                    ActionId = "apply",
                    Outcome = RuleActionExecutionOutcome.Applied,
                    UpdatedAtUtc = now
                },
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome",
                    ActionId = "simulate",
                    Mode = RuleExecutionMode.DryRun,
                    Outcome = RuleActionExecutionOutcome.WouldExecute,
                    UpdatedAtUtc = now
                }
            ],
            PreparedActions =
            [
                new RuleExecutionAuditPreparationDto
                {
                    ActionId = "greet",
                    Outcome = RuleActionExecutionOutcome.OutcomeUnknown,
                    CompletedFragmentCount = 1,
                    TotalFragmentCount = 2
                }
            ]
        };
        var completed = new RuleExecutionAuditDto
        {
            ExecutionId = completedId,
            GameServerId = scenario.GameServerId,
            FeatureId = "vpn-protection",
            State = RuleFeatureExecutionState.Completed,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Diagnostics = [new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.NoMatch }]
        };
        var noMatch = new RuleExecutionAuditDto
        {
            ExecutionId = noMatchId,
            GameServerId = scenario.GameServerId,
            FeatureId = "vpn-protection",
            MatchStatus = RuleExecutionMatchStatus.NoMatch,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var audit = Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1);
        audit.Setup(api => api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>([waiting, completed, noMatch]))));
        audit.Setup(api => api.GetRuleExecutionFlow(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK,
                new ApiResponse<RuleExecutionFlowDto>(new RuleExecutionFlowDto
                {
                    Execution = id == waitingId ? waiting : completed,
                    StructureStatus = RuleExecutionStructureStatus.Missing
                })));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/RulesAudit").AbsoluteUri);
        var waitingSection = page.Locator($"[data-execution-id='{waitingId}']");
        await Assertions.Expect(waitingSection).ToContainTextAsync("welcome-messages");
        await waitingSection.GetByText("View execution flow", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(waitingSection).ToContainTextAsync("Individual condition results are not available");
        await Assertions.Expect(waitingSection).ToContainTextAsync("they do not establish the order");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Waiting for protection evidence");
        await Assertions.Expect(waitingSection).ToContainTextAsync("FallbackUsed");
        await Assertions.Expect(waitingSection).ToContainTextAsync("context.player.Country");
        await Assertions.Expect(waitingSection.GetByText("7 additional diagnostics omitted.", new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(waitingSection).ToContainTextAsync("<b>Protection pending</b>");
        await Assertions.Expect(waitingSection).ToContainTextAsync("OutcomeUnknown - 1 / 2 fragments completed");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Item 1, fragment 2");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Fragment result unknown");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Dispatched; application is not confirmed.");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Applied, as recorded by the execution service.");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Simulated; no live application is implied.");
        await Assertions.Expect(waitingSection.Locator("img, script, b")).ToHaveCountAsync(0);
        var completedSection = page.Locator($"[data-execution-id='{completedId}']");
        await Assertions.Expect(completedSection).ToContainTextAsync("vpn-protection");
        await Assertions.Expect(completedSection.Locator("tr").First.Locator("td").Last).ToHaveTextAsync("Unknown");
        await Assertions.Expect(page.Locator($"[data-execution-id='{noMatchId}'] tr").First.Locator("td").Last).ToHaveTextAsync("No match");
        await completedSection.GetByText("View execution flow", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(completedSection).ToContainTextAsync("NoMatch");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task RulesAudit_FiltersAndPagesWithoutLosingUtcBounds()
    {
        var scenario = new FileTransportScenario();
        var items = Enumerable.Range(1, 21).Select(index => new RuleExecutionAuditDto
        {
            ExecutionId = Guid.NewGuid(),
            GameServerId = scenario.GameServerId,
            FeatureId = "welcome-messages",
            Username = $"Player {index}",
            MatchStatus = RuleExecutionMatchStatus.Matched,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)
        }).ToArray();
        var audit = Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1);
        audit.Setup(api => api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuleExecutionAuditQueryDto query, CancellationToken _) =>
                new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                    new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(
                        new CollectionModel<RuleExecutionAuditDto>(items
                            .Where(item => !query.MatchStatus.HasValue || item.MatchStatus == query.MatchStatus)
                            .Skip(query.SkipEntries).Take(query.TakeEntries).ToArray()))));
        audit.Setup(api => api.GetRuleExecutionFlow(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK,
                    new ApiResponse<RuleExecutionFlowDto>(new RuleExecutionFlowDto
                    {
                        Execution = items.Single(item => item.ExecutionId == id)
                    })));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/RulesAudit").AbsoluteUri);
        await page.GetByLabel("Server", new() { Exact = true }).SelectOptionAsync(scenario.GameServerId.ToString());
        await page.GetByLabel("Feature ID").FillAsync("welcome-messages");
        await page.GetByLabel("Rule matched").SelectOptionAsync("Matched");
        await page.GetByLabel("Created from (UTC, inclusive)").FillAsync("2026-01-01T10:00");
        await page.GetByLabel("Created before (UTC, exclusive)").FillAsync("2026-01-02T10:00");
        await page.GetByRole(AriaRole.Button, new() { Name = "Filter" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Next" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Next" }).ClickAsync();
        await Assertions.Expect(page.Locator($"[data-execution-id='{items[20].ExecutionId}']")).ToContainTextAsync("Player 21");
        await Assertions.Expect(page.GetByLabel("Feature ID")).ToHaveValueAsync("welcome-messages");
        await Assertions.Expect(page.GetByLabel("Rule matched")).ToHaveValueAsync("Matched");
        await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Matched", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Server", new() { Exact = true })).ToHaveValueAsync(scenario.GameServerId.ToString());
        audit.Verify(api => api.SearchRuleExecutionAudit(It.Is<RuleExecutionAuditQueryDto>(query =>
            query.GameServerId == scenario.GameServerId && query.FeatureId == "welcome-messages" &&
            query.MatchStatus == RuleExecutionMatchStatus.Matched &&
            query.FromUtc == new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) &&
            query.ToUtc == new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc) &&
            query.SkipEntries == 20), It.IsAny<CancellationToken>()), Times.Once);
        await page.Locator($"[data-execution-id='{items[20].ExecutionId}'] summary").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Open full execution page" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Rules Execution Flow" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Back to audit" }).ClickAsync();
        await Assertions.Expect(page.Locator($"[data-execution-id='{items[20].ExecutionId}']")).ToContainTextAsync("Player 21");
        await Assertions.Expect(page.GetByLabel("Feature ID")).ToHaveValueAsync("welcome-messages");
        await Assertions.Expect(page.GetByLabel("Rule matched")).ToHaveValueAsync("Matched");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task RulesAudit_OpensHistoricalRuleFlowWithoutInventingConditionOutcomes()
    {
        var scenario = new FileTransportScenario();
        var execution = new RuleExecutionAuditDto
        {
            ExecutionId = Guid.NewGuid(),
            GameServerId = scenario.GameServerId,
            FeatureId = "welcome-messages",
            TriggerId = "player.connected",
            Diagnostics =
            [
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.NoMatch, RuleId = "other", ObservedAtUtc = DateTimeOffset.UtcNow },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.WaitingForContext, RuleId = "welcome", ObservedAtUtc = DateTimeOffset.UtcNow },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.Expired, RuleId = "welcome", ObservedAtUtc = DateTimeOffset.UtcNow },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.WaitingForContext, RuleId = "welcome", ActionId = "greet", ObservedAtUtc = DateTimeOffset.UtcNow },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.Expired, RuleId = "welcome", ActionId = "greet", ObservedAtUtc = DateTimeOffset.UtcNow },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.WaitingForContext, RuleId = "only-action", ActionId = "send", ObservedAtUtc = DateTimeOffset.UtcNow }
            ],
            Actions =
            [
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome", ActionId = "greet", RuleIndex = 0, ActionIndex = 0,
                    Outcome = RuleActionExecutionOutcome.Dispatched, Mode = RuleExecutionMode.Enforce
                },
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome", ActionId = "simulate", RuleIndex = 0, ActionIndex = 2,
                    Outcome = RuleActionExecutionOutcome.WouldExecute, Mode = RuleExecutionMode.DryRun
                },
                new RuleExecutionAuditActionDto
                {
                    RuleId = "welcome", ActionId = "uncertain", RuleIndex = 0, ActionIndex = 3,
                    Outcome = RuleActionExecutionOutcome.OutcomeUnknown, Mode = RuleExecutionMode.Enforce
                }
            ]
        };
        var flow = new RuleExecutionFlowDto
        {
            Execution = execution,
            StructureStatus = RuleExecutionStructureStatus.Available,
            RuleSetRevision = "revision-1",
            Rules =
            [
                new RuleExecutionFlowRuleDto
                {
                    Id = "welcome", RuleIndex = 0, Enabled = true, AfterMatch = "StopProcessingRules",
                    Condition = new RuleExecutionFlowConditionDto
                    {
                        GroupOperator = "And",
                        Conditions =
                        [
                            new RuleExecutionFlowConditionDto
                            {
                                ConditionId = "player.first-seen-guid", DisplayName = "GUID first seen",
                                Operator = "WithinAge",
                                SafeParameters = [new RuleExecutionFlowParameterDto { Name = "Duration", Value = "7.00:00:00" }],
                                HasUnavailableParameters = true
                            },
                            new RuleExecutionFlowConditionDto
                            {
                                ConditionId = "unknown", DisplayName = "<img src=x>",
                                SafeParameters = [new RuleExecutionFlowParameterDto { Name = "Value", Value = "<script>unsafe</script>" }]
                            }
                        ]
                    },
                    Actions =
                    [
                        new RuleExecutionFlowActionDto { Id = "greet", TypeId = "message.private", DependsOnActionIds = [] },
                        new RuleExecutionFlowActionDto { Id = "ungated", ActionIndex = 1, TypeId = "message.private" },
                        new RuleExecutionFlowActionDto { Id = "simulate", ActionIndex = 2, TypeId = "message.private" },
                        new RuleExecutionFlowActionDto { Id = "uncertain", ActionIndex = 3, TypeId = "message.private" }
                    ]
                },
                new RuleExecutionFlowRuleDto
                {
                    Id = "other", RuleIndex = 1,
                    Condition = new RuleExecutionFlowConditionDto { ConditionId = "other-condition" }
                },
                new RuleExecutionFlowRuleDto
                {
                    Id = "only-action", RuleIndex = 2,
                    Condition = new RuleExecutionFlowConditionDto { ConditionId = "only-action-condition" },
                    Actions = [new RuleExecutionFlowActionDto { Id = "send", TypeId = "message.private" }]
                }
            ]
        };
        var audit = Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1);
        audit.Setup(api => api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>([execution]))));
        audit.Setup(api => api.GetRuleExecutionFlow(execution.ExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, "/RulesAudit").AbsoluteUri);
        audit.Verify(api => api.GetRuleExecutionFlow(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        var section = page.Locator($"[data-execution-id='{execution.ExecutionId}']");
        var toggle = section.GetByText("View execution flow", new() { Exact = true });
        await toggle.ClickAsync();
        await Assertions.Expect(section.Locator("[data-execution-flow]")).ToBeVisibleAsync();
        await Assertions.Expect(section.Locator(".rules-flow__trigger")).ToContainTextAsync("player.connected");
        var rule = page.Locator("[data-rule-id='welcome']");
        await Assertions.Expect(rule).ToContainTextAsync("GUID first seen - WithinAge");
        await Assertions.Expect(rule.Locator(".rules-flow__condition .badge")).ToHaveCountAsync(0);
        await Assertions.Expect(rule).ToContainTextAsync("Duration: 7.00:00:00");
        await Assertions.Expect(rule).ToContainTextAsync("Some condition values are unavailable");
        await Assertions.Expect(rule).ToContainTextAsync("No match outcome recorded");
        await Assertions.Expect(rule.Locator(".rules-flow__decision")).ToContainTextAsync("Waiting for rule context recorded");
        await Assertions.Expect(rule.Locator(".rules-flow__decision")).ToContainTextAsync("Rule freshness window expiry recorded");
        await Assertions.Expect(page.Locator("[data-rule-id='other']")).ToContainTextAsync("No match recorded");
        await Assertions.Expect(rule).ToContainTextAsync("Dispatched - application not confirmed");
        await Assertions.Expect(rule.Locator("[data-action-id='greet']")).ToContainTextAsync("Recorded diagnostic: WaitingForContext");
        await Assertions.Expect(rule.Locator("[data-action-id='greet']")).ToContainTextAsync("Recorded diagnostic: Expired");
        await Assertions.Expect(rule.Locator("[data-action-id='ungated']")).ToContainTextAsync("No action outcome recorded");
        await Assertions.Expect(rule.Locator("[data-action-id='ungated']")).Not.ToContainTextAsync("Recorded diagnostic");
        await Assertions.Expect(rule.Locator("[data-action-id='uncertain']")).ToContainTextAsync("Outcome unknown - may have run");
        var onlyActionRule = page.Locator("[data-rule-id='only-action']");
        await Assertions.Expect(onlyActionRule.Locator(".rules-flow__decision")).ToContainTextAsync("No match outcome recorded");
        await Assertions.Expect(onlyActionRule.Locator(".rules-flow__decision")).Not.ToContainTextAsync("Waiting for rule context recorded");
        await Assertions.Expect(onlyActionRule.Locator("[data-action-id='send']")).ToContainTextAsync("Recorded diagnostic: WaitingForContext");
        await Assertions.Expect(rule).ToContainTextAsync("<script>unsafe</script>");
        await Assertions.Expect(rule.Locator("script, img")).ToHaveCountAsync(0);
        await page.SetViewportSizeAsync(390, 844);
        var path = rule.Locator(".rules-flow__path");
        await Assertions.Expect(rule.Locator("[data-action-id='simulate']")).ToContainTextAsync("Simulated (WouldExecute)");
        await Assertions.Expect(path).ToHaveCSSAsync("grid-template-columns", new System.Text.RegularExpressions.Regex(@"^[\d.]+px$"));
        Assert.True(await section.Locator(".rules-flow").EvaluateAsync<bool>("el => el.getBoundingClientRect().right <= el.closest('.table-responsive').getBoundingClientRect().right + 1"));
        Assert.True(await section.Locator(".rules-flow").EvaluateAsync<bool>("el => { const container = el.closest('.table-responsive'); container.scrollLeft = container.scrollWidth; const flow = el.getBoundingClientRect(); const viewport = container.getBoundingClientRect(); return flow.left >= viewport.left - 1 && flow.right <= viewport.right + 1; }"));
        await toggle.ClickAsync();
        await Assertions.Expect(path).ToBeHiddenAsync();
        await toggle.ClickAsync();
        await Assertions.Expect(path).ToBeVisibleAsync();
        audit.Verify(api => api.GetRuleExecutionFlow(execution.ExecutionId, It.IsAny<CancellationToken>()), Times.Once);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task RulesAudit_DoesNotInferActionOutcomeFromNoMatchDiagnostic()
    {
        var scenario = new FileTransportScenario();
        var id = Guid.NewGuid();
        var flow = new RuleExecutionFlowDto
        {
            Execution = new RuleExecutionAuditDto
            {
                ExecutionId = id,
                State = RuleFeatureExecutionState.Completed,
                MatchStatus = RuleExecutionMatchStatus.NoMatch,
                Diagnostics = [new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.NoMatch, RuleId = "rule", ObservedAtUtc = DateTimeOffset.UtcNow }]
            },
            StructureStatus = RuleExecutionStructureStatus.Available,
            Rules =
            [
                new RuleExecutionFlowRuleDto
                {
                    Id = "rule",
                    Condition = new RuleExecutionFlowConditionDto { ConditionId = "condition" },
                    Actions = [new RuleExecutionFlowActionDto { Id = "send", TypeId = "message.private" }]
                }
            ]
        };
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/RulesAudit/Execution/{id}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("[data-rule-id='rule'] .rules-flow__decision")).ToContainTextAsync("No match recorded");
        await Assertions.Expect(browser.Page.Locator("[data-action-id='send']")).ToContainTextAsync("No action outcome recorded");
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(RuleExecutionStructureStatus.Missing, "Historical rule structure was not recorded")]
    [InlineData(RuleExecutionStructureStatus.Malformed, "Historical rule structure is malformed")]
    [InlineData(RuleExecutionStructureStatus.Available, "The recorded rule set is empty")]
    public async Task RulesAudit_DistinguishesUnavailableAndEmptyRuleStructure(RuleExecutionStructureStatus status, string message)
    {
        var scenario = new FileTransportScenario();
        var id = Guid.NewGuid();
        var flow = new RuleExecutionFlowDto
        {
            Execution = new RuleExecutionAuditDto { ExecutionId = id },
            StructureStatus = status
        };
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/RulesAudit/Execution/{id}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("main")).ToContainTextAsync(message);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task RulesAudit_RetriesUnavailableInlineFlow()
    {
        var scenario = new FileTransportScenario();
        var execution = new RuleExecutionAuditDto
        {
            ExecutionId = Guid.NewGuid(),
            GameServerId = scenario.GameServerId,
            FeatureId = "vpn-protection"
        };
        var audit = Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1);
        audit.Setup(api => api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>([execution]))));
        audit.Setup(api => api.GetRuleExecutionFlow(execution.ExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK,
                new ApiResponse<RuleExecutionFlowDto>(new RuleExecutionFlowDto { Execution = execution })));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var attempts = 0;
        await browser.Page.RouteAsync("**/RulesAudit/Execution/**", async route =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                await route.FulfillAsync(new RouteFulfillOptions { Status = 503 });
            else
                await route.ContinueAsync();
        });

        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/RulesAudit").AbsoluteUri);
        var section = browser.Page.Locator($"[data-execution-id='{execution.ExecutionId}']");
        await section.GetByText("View execution flow", new() { Exact = true }).ClickAsync();
        await Assertions.Expect(section.GetByText("Data unavailable.", new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(section.GetByRole(AriaRole.Link, new() { Name = "Open full execution page" })).ToBeVisibleAsync();
        await section.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Assertions.Expect(section.Locator("[data-execution-flow]")).ToBeVisibleAsync();
        Assert.Equal(2, attempts);
        audit.Verify(api => api.GetRuleExecutionFlow(execution.ExecutionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Editor_PreservesLiteralText_AndEditsRecipientsMessagesAndTiming()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var page = browser.Page;
        await page.SetContentAsync("""
            <form>
              <div id="editor" data-rules-catalogue-editor data-feature-id="welcome-messages"
                   data-override="false" data-server-context="false" data-show-inherited-rules="false">
                <input type="hidden" data-rules-value>
                <div data-rules-editor></div>
              </div>
            </form>
            """);
        await page.EvaluateAsync("""
            () => {
                const root = document.querySelector('#editor');
                const parameter = (name, type, extra = {}) => ({ name, type, allowedValues: [], ...extra });
                root.dataset.catalogue = JSON.stringify({
                    features: [{ featureId: 'welcome-messages', conditionIds: ['always'], actionTypeIds: ['message.private-tagged'] }],
                    conditions: [{ conditionId: 'always', displayName: 'Everyone', parameters: [] }],
                    actions: [{
                        actionTypeId: 'message.private-tagged', displayName: 'Private message tagged players',
                        parameters: [
                            parameter('recipientTags', 'string[]', { required: true }),
                            parameter('recipientTagMatch', 'string', { allowedValues: ['Any', 'All'], required: true }),
                            parameter('message', 'string', { supportsTemplates: true, supportsMessageList: true }),
                            parameter('messages', 'string[]', { supportsTemplates: true, supportsMessageList: true })
                        ]
                    }]
                });
                root.dataset.tags = JSON.stringify([{ value: 'tag-a', text: 'Admin' }, { value: 'tag-b', text: 'Moderator' }]);
                root.dataset.tokens = JSON.stringify([
                    { token: '{context.player.Name}', label: 'Player name', group: 'Player',
                      description: 'Triggering player', sampleValue: 'Example', fallback: 'player', availability: 'On connect' },
                    { token: '{context.player.IpAddress}', label: 'IP address', group: 'Player',
                      isSensitive: true, fallback: 'unknown' }
                ]);
                root.dataset.compatibility = '{}';
                root.dataset.globalRules = '[]';
                root.dataset.gameTypes = '{}';
                root.querySelector('[data-rules-value]').value = JSON.stringify([{
                    id: 'welcome', enabled: true, order: 0,
                    condition: { conditionId: 'always', parameters: {} },
                    actions: [{ id: 'alert', typeId: 'message.private-tagged', dependsOnActionIds: [],
                        parameters: { recipientTags: ['tag-a'], recipientTagMatch: 'Any',
                            message: 'literal {braces}', messages: ['first', 'second'] } }],
                    afterMatch: 'StopProcessingRules'
                }]);
            }
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/rules-catalogue-editor.js").AbsoluteUri });

        var editor = page.Locator("#editor");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await editor.GetByLabel("Recipient tags").SelectOptionAsync(["tag-a", "tag-b"]);
        await editor.GetByLabel("Recipient tag match").SelectOptionAsync("All");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Move message down" }).First.ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Add message" }).ClickAsync();
        await editor.GetByLabel("Message 3").FillAsync("third");
        var thirdMessage = editor.Locator("div.border.rounded.p-2.mb-2").Nth(2);
        await thirdMessage.Locator("summary").ClickAsync();
        await thirdMessage.GetByLabel("Search tokens").FillAsync("name");
        await Assertions.Expect(thirdMessage.GetByText("IP address", new() { Exact = true })).Not.ToBeVisibleAsync();
        await thirdMessage.GetByRole(AriaRole.Button, new() { Name = "Player name" }).ClickAsync();
        await Assertions.Expect(editor.GetByLabel("Use tokens in messages")).ToBeCheckedAsync();
        await editor.GetByLabel("Connection delay override (seconds)").FillAsync("61");
        using (var invalid = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync()))
            Assert.Equal(61, invalid.RootElement[0].GetProperty("timing").GetProperty("connectionDelaySeconds").GetInt32());
        Assert.False(await page.EvaluateAsync<bool>("document.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))"));
        await editor.GetByLabel("Connection delay override (seconds)").FillAsync("12");

        var json = await editor.Locator("[data-rules-value]").InputValueAsync();
        using var saved = JsonDocument.Parse(json);
        var rule = saved.RootElement[0];
        var action = rule.GetProperty("actions")[0];
        Assert.Equal("literal {braces}", action.GetProperty("parameters").GetProperty("message").GetString());
        Assert.Equal(["second", "first", "third{context.player.Name}"], action.GetProperty("parameters").GetProperty("messages").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["tag-a", "tag-b"], action.GetProperty("parameters").GetProperty("recipientTags").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("All", action.GetProperty("parameters").GetProperty("recipientTagMatch").GetString());
        Assert.Equal(["messages"], action.GetProperty("templateParameters").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(12, rule.GetProperty("timing").GetProperty("connectionDelaySeconds").GetInt32());
        await editor.GetByLabel("Use tokens in message", new() { Exact = true }).CheckAsync();
        await editor.GetByLabel("Message", new() { Exact = true }).FillAsync("");
        using var listOnly = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync());
        var listOnlyAction = listOnly.RootElement[0].GetProperty("actions")[0];
        Assert.False(listOnlyAction.GetProperty("parameters").TryGetProperty("message", out _));
        Assert.Equal(3, listOnlyAction.GetProperty("parameters").GetProperty("messages").GetArrayLength());
        Assert.Equal(["messages"], listOnlyAction.GetProperty("templateParameters").EnumerateArray().Select(value => value.GetString()));
        await Assertions.Expect(editor.GetByLabel("Use tokens in message", new() { Exact = true })).Not.ToBeCheckedAsync();
        await editor.GetByLabel("Use tokens in message", new() { Exact = true }).CheckAsync();
        using var enabledEmpty = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync());
        var emptyAction = enabledEmpty.RootElement[0].GetProperty("actions")[0];
        Assert.Equal("", emptyAction.GetProperty("parameters").GetProperty("message").GetString());
        Assert.Contains("message", emptyAction.GetProperty("templateParameters").EnumerateArray().Select(value => value.GetString()));
        await editor.GetByLabel("Use tokens in message", new() { Exact = true }).UncheckAsync();
        using var disabledEmpty = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync());
        Assert.False(disabledEmpty.RootElement[0].GetProperty("actions")[0].GetProperty("parameters").TryGetProperty("message", out _));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Editor_ChangingUnknownAction_RemovesUnsupportedTemplateParameters()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var page = browser.Page;
        await page.SetContentAsync("""
            <form><div id="editor" data-rules-catalogue-editor data-feature-id="welcome-messages"
                data-override="false" data-server-context="false">
                <input type="hidden" data-rules-value><div data-rules-editor></div>
            </div></form>
            """);
        await page.EvaluateAsync("""
            () => {
                const root = document.querySelector('#editor');
                root.dataset.catalogue = JSON.stringify({
                    features: [{ featureId: 'welcome-messages', conditionIds: ['always'], actionTypeIds: ['message.private'] }],
                    conditions: [{ conditionId: 'always', displayName: 'Everyone', parameters: [] }],
                    actions: [{ actionTypeId: 'message.private', displayName: 'Private message',
                        parameters: [{ name: 'message', type: 'string', supportsTemplates: true, allowedValues: [] }] }]
                });
                root.dataset.tags = '[]';
                root.dataset.compatibility = '{}';
                root.dataset.globalRules = '[]';
                root.querySelector('[data-rules-value]').value = JSON.stringify([{
                    id: 'rule', enabled: true, order: 0,
                    condition: { conditionId: 'always', parameters: {} },
                    actions: [{ id: 'action', typeId: 'old.unknown', parameters: {}, dependsOnActionIds: [],
                        templateParameters: ['reason', 'message'] }],
                    afterMatch: 'StopProcessingRules'
                }]);
            }
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(browser.Host.BaseAddress, "/js/rules-catalogue-editor.js").AbsoluteUri });
        var editor = page.Locator("#editor");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Expand all" }).ClickAsync();
        await editor.GetByLabel("Action type").SelectOptionAsync("message.private");
        using var saved = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync());
        Assert.Equal(["message"], saved.RootElement[0].GetProperty("actions")[0].GetProperty("templateParameters")
            .EnumerateArray().Select(value => value.GetString()));
        browser.AssertNoBrowserErrors();
    }
}
