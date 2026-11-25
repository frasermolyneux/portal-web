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
        await Assertions.Expect(editor).ToContainTextAsync("Effective at page load: 5 second delay; 120 second freshness");
        var preview = editor.Locator("[data-action-preview='message']");
        await Assertions.Expect(preview).ToContainTextAsync("Hello ExamplePlayer");
        await Assertions.Expect(preview).ToContainTextAsync("RCON preview for CallOfDuty4: 149 total bytes, including 32 reserved prefix bytes.");
        await Assertions.Expect(preview).ToContainTextAsync("another provider");
        await preview.GetByLabel("Message preview data").SelectOptionAsync("missing");
        await Assertions.Expect(preview).ToContainTextAsync("Hello player");
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
    public async Task ExecutionDiagnostics_ShowWaitingAndNoActionReasonsWithoutParsingSnapshots()
    {
        var scenario = new FileTransportScenario();
        var waitingId = Guid.NewGuid();
        var completedId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        RuleFeatureExecutionDto Execution(Guid id, RuleFeatureExecutionState state) => new()
        {
            ExecutionId = id,
            GameServerId = scenario.GameServerId,
            FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature,
            State = state,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Trigger = new RuleTriggerEnvelope { Username = "<img src=x>" },
            Coordination = new RuleExecutionCoordination
            {
                DueAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(2),
                WaitingReason = state == RuleFeatureExecutionState.Waiting ? "Waiting for protection evidence" : "Obsolete waiting reason"
            },
            DecisionSnapshotJson = /*lang=json,strict*/ """{"internal":"Do not display opaque processor state"}"""
        };
        var waiting = Execution(waitingId, RuleFeatureExecutionState.Waiting) with
        {
            Diagnostics =
            [
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.WaitingForProtection, Detail = "<b>Protection pending</b>" },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.FallbackUsed, TokenKey = "context.player.Country", Source = "GeoLocation", Detail = "Fallback selected" },
                new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.DiagnosticsOmitted, OmittedCount = 7 }
            ],
            Actions =
            [
                new RuleActionExecutionDto
                {
                    RuleId = "welcome",
                    ActionId = "greet",
                    Outcome = RuleActionExecutionOutcome.OutcomeUnknown,
                    Detail = "Fragment result unknown",
                    Command = new RuleActionCommand
                    {
                        DeliveryIdentity = new RuleDeliveryIdentity
                        {
                            MessageIndex = 0,
                            FragmentIndex = 1,
                            RecipientConnectionId = recipientId
                        }
                    }
                }
            ],
            PreparedActions =
            [
                new RulePreparedActionDto
                {
                    ActionId = "greet",
                    Outcome = RuleActionExecutionOutcome.OutcomeUnknown,
                    CompletedFragmentCount = 1,
                    TotalFragmentCount = 2,
                    Commands =
                    [
                        new RuleActionCommand
                        {
                            Effect = new RuleNativeEffect { Type = RuleNativeEffectType.PrivateMessage, Message = "<script>unsafe</script>" },
                            DeliveryIdentity = new RuleDeliveryIdentity { MessageIndex = 0, FragmentIndex = 1 }
                        }
                    ]
                }
            ]
        };
        var completed = Execution(completedId, RuleFeatureExecutionState.Completed) with
        {
            Diagnostics = [new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.NoMatch }]
        };
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.SearchRuleExecutions(It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleFeatureExecutionDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleFeatureExecutionDto>>(new CollectionModel<RuleFeatureExecutionDto>([waiting, completed]))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);
        var page = browser.Page;
        await page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/WelcomeExecutions?id={scenario.GameServerId}").AbsoluteUri);
        var waitingSection = page.Locator($"[data-execution-id='{waitingId}']");
        await Assertions.Expect(waitingSection).ToContainTextAsync("Waiting for protection evidence");
        await Assertions.Expect(waitingSection).ToContainTextAsync("FallbackUsed");
        await Assertions.Expect(waitingSection).ToContainTextAsync("context.player.Country");
        await Assertions.Expect(waitingSection.Locator(".alert-warning")).ToContainTextAsync("7 additional diagnostics omitted");
        await Assertions.Expect(waitingSection).ToContainTextAsync("<b>Protection pending</b>");
        await waitingSection.Locator("summary").ClickAsync();
        await Assertions.Expect(waitingSection).ToContainTextAsync("OutcomeUnknown - 1 / 2 fragments completed");
        await Assertions.Expect(waitingSection.Locator("pre")).ToHaveTextAsync("<script>unsafe</script>");
        var outcomeRow = waitingSection.Locator("tr", new() { HasText = "Fragment result unknown" });
        await Assertions.Expect(outcomeRow).ToContainTextAsync("Item 1, fragment 2");
        await Assertions.Expect(outcomeRow).ToContainTextAsync(recipientId.ToString());
        await Assertions.Expect(outcomeRow).ToContainTextAsync("OutcomeUnknown");
        await Assertions.Expect(waitingSection.Locator("img, script, b")).ToHaveCountAsync(0);
        var completedSection = page.Locator($"[data-execution-id='{completedId}']");
        await Assertions.Expect(completedSection).ToContainTextAsync("NoMatch");
        await Assertions.Expect(completedSection).Not.ToContainTextAsync("Obsolete waiting reason");
        await Assertions.Expect(page.Locator("body")).Not.ToContainTextAsync("Do not display opaque processor state");
        browser.AssertNoBrowserErrors();
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
        await editor.GetByLabel("Action type").SelectOptionAsync("message.private");
        using var saved = JsonDocument.Parse(await editor.Locator("[data-rules-value]").InputValueAsync());
        Assert.Equal(["message"], saved.RootElement[0].GetProperty("actions")[0].GetProperty("templateParameters")
            .EnumerateArray().Select(value => value.GetString()));
        browser.AssertNoBrowserErrors();
    }
}
