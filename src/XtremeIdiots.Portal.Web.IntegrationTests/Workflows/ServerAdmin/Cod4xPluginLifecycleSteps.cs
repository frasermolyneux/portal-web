using Microsoft.Playwright;
using Reqnroll;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Binding]
public sealed class Cod4xPluginLifecycleSteps
{
    private BrowserFixture? browser;
    private string? directResponseBody;
    private Task<IResponse>? pendingResponse;
    private string? profile;
    private Cod4xPluginLifecycleScenario? scenario;

    [Given("a successful CoD4x lifecycle scenario for a direct lifecycle user")]
    public void GivenSuccessfulDirectLifecycleScenario()
    {
        Configure(TestPrincipalProfiles.Cod4xLifecycleManager);
    }

    [Given("a successful CoD4x lifecycle scenario for a head admin")]
    public void GivenSuccessfulHeadAdminScenario()
    {
        Configure(TestPrincipalProfiles.HeadAdmin);
    }

    [Given("a successful CoD4x lifecycle scenario for a game admin")]
    public void GivenSuccessfulGameAdminScenario()
    {
        Configure(TestPrincipalProfiles.GameAdmin);
    }

    [Given("a CoD4x lifecycle scenario with malformed current settings")]
    public void GivenMalformedSettingsScenario()
    {
        profile = TestPrincipalProfiles.Cod4xLifecycleManager;
        scenario = new Cod4xPluginLifecycleScenario(malformedConfiguration: true);
    }

    [Given("a CoD4x lifecycle scenario with unavailable current settings")]
    public void GivenUnavailableSettingsScenario()
    {
        profile = TestPrincipalProfiles.Cod4xLifecycleManager;
        scenario = new Cod4xPluginLifecycleScenario(configurationLoadSucceeds: false);
    }

    [Given("a failing CoD4x lifecycle repository scenario")]
    public void GivenFailingRepositoryScenario()
    {
        profile = TestPrincipalProfiles.Cod4xLifecycleManager;
        scenario = new Cod4xPluginLifecycleScenario(upsertSucceeds: false);
    }

    [Given("a CoD4x lifecycle scenario with an existing pending request")]
    public void GivenExistingPendingRequestScenario()
    {
        profile = TestPrincipalProfiles.Cod4xLifecycleManager;
        scenario = new Cod4xPluginLifecycleScenario(pendingRequest: true);
    }

    [Given("a delayed CoD4x lifecycle repository scenario")]
    public void GivenDelayedRepositoryScenario()
    {
        profile = TestPrincipalProfiles.Cod4xLifecycleManager;
        scenario = new Cod4xPluginLifecycleScenario(upsertDelayMilliseconds: 500);
    }

    [When("the user requests install of version {string}")]
    public async Task WhenUserRequestsInstall(string version)
    {
        await OpenLifecyclePanelAsync();
        await Browser.Page.Locator("#sd-cod4x-targetVersion").FillAsync(version);
        await ClickLifecycleActionAsync("cod4x-install");
    }

    [When("the user requests the {string} lifecycle action")]
    public async Task WhenUserRequestsLifecycleAction(string action)
    {
        await OpenLifecyclePanelAsync();
        await ClickLifecycleActionAsync($"cod4x-{action.ToLowerInvariant()}");
    }

    [When("the user opens the CoD4x lifecycle panel")]
    public async Task WhenUserOpensLifecyclePanel()
    {
        await OpenLifecyclePanelAsync();
    }

    [When("the user directly submits a forged rollback request")]
    public async Task WhenUserForgesRollback()
    {
        await OpenLifecyclePanelAsync();
        await SubmitDirectlyAsync("Rollback", null);
    }

    [When("the user requests install without a target version")]
    public async Task WhenUserRequestsInstallWithoutVersion()
    {
        await OpenLifecyclePanelAsync();
        await Browser.Page.Locator("#sd-cod4x-targetVersion").FillAsync(string.Empty);
        await Browser.Page.GetByTestId("cod4x-install").ClickAsync();
    }

    [When("the user directly submits install version {string}")]
    public async Task WhenUserDirectlySubmitsInstall(string version)
    {
        await OpenLifecyclePanelAsync();
        await SubmitDirectlyAsync("Install", version);
    }

    [When("the user directly submits a rollback request")]
    public async Task WhenUserDirectlySubmitsRollback()
    {
        await SubmitDirectlyAsync("Rollback", null);
    }

    [When("the user starts a rollback lifecycle request")]
    public async Task WhenUserStartsRollbackRequest()
    {
        await OpenLifecyclePanelAsync();
        pendingResponse = Browser.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath.Equals("/ServerAdmin/RequestCod4xPluginOperation", StringComparison.Ordinal));
        await Browser.Page.GetByTestId("cod4x-rollback").ClickAsync();
    }

    [When("the lifecycle request completes")]
    public async Task WhenLifecycleRequestCompletes()
    {
        await (pendingResponse ?? throw new InvalidOperationException("Lifecycle response was not started."));
    }

    [Then("an {string} lifecycle request should be queued for version {string}")]
    public void ThenRequestQueuedForVersion(string action, string version)
    {
        var request = Scenario.GetAttemptedDocument().OperationRequest;
        Assert.NotNull(request);
        Assert.Equal(Enum.Parse<Cod4xPluginOperationAction>(action), request.Action);
        Assert.Equal(version, request.TargetVersion);
        Assert.False(string.IsNullOrWhiteSpace(request.OperationId));
        Assert.Equal("Portal Test User", request.RequestedBy);
    }

    [Then("an {string} lifecycle request should be queued without a target version")]
    public void ThenRequestQueuedWithoutVersion(string action)
    {
        var request = Scenario.GetAttemptedDocument().OperationRequest;
        Assert.NotNull(request);
        Assert.Equal(Enum.Parse<Cod4xPluginOperationAction>(action), request.Action);
        Assert.Null(request.TargetVersion);
    }

    [Then("the lifecycle request should preserve runtime state")]
    public void ThenRuntimeStatePreserved()
    {
        var runtime = Scenario.GetAttemptedDocument().RuntimeState;
        Assert.NotNull(runtime);
        Assert.Equal("1.2.3", runtime.CurrentVersion);
        Assert.Equal("1.2.2", runtime.PreviousKnownGoodVersion);
        Assert.Equal("previous-operation", runtime.LastOperationId);
        Assert.Equal(Cod4xPluginOperationStatus.Succeeded, runtime.LastOperationStatus);
    }

    [Then("the lifecycle request should preserve plugin settings and schema")]
    public void ThenPluginSettingsPreserved()
    {
        var document = Scenario.GetAttemptedDocument();
        Assert.Equal(Cod4xPluginSettingsConstants.SchemaVersion, document.SchemaVersion);
        Assert.True(document.Enabled);
        Assert.Equal("/plugins", document.PluginRootDirectory);
        Assert.NotNull(document.OperationRequest?.RequestedAtUtc);
    }

    [Then("the install request should contain Linux artifact metadata")]
    public void ThenInstallContainsArtifactMetadata()
    {
        var extensionData = Scenario.GetAttemptedDocument().OperationRequest?.ExtensionData;
        Assert.NotNull(extensionData);
        Assert.Equal("releases/1.2.4/linux/x86/portal-cod4x-plugin.so", extensionData["artifactBlobPath"].GetString());
        Assert.Contains("1.2.4", extensionData["artifactPath"].GetString(), StringComparison.Ordinal);
        Assert.EndsWith(".so", extensionData["artifactPath"].GetString(), StringComparison.Ordinal);
        Assert.Equal(2, extensionData.Count);
    }

    [Then("the versionless lifecycle request should contain no artifact metadata")]
    public void ThenVersionlessRequestHasNoArtifactMetadata()
    {
        Assert.Null(Scenario.GetAttemptedDocument().OperationRequest?.ExtensionData);
    }

    [Then("the CoD4x lifecycle controls should not be present")]
    public async Task ThenControlsNotPresent()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-install")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-rollback")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-unload")).ToHaveCountAsync(0);
    }

    [Then("the lifecycle permission message should be visible")]
    public async Task ThenPermissionMessageVisible()
    {
        await Assertions.Expect(Browser.Page.GetByText("You do not have permission to request plugin lifecycle operations.")).ToBeVisibleAsync();
    }

    [Then("the lifecycle controls should be disabled")]
    [Then("all lifecycle controls should be disabled in flight")]
    [Then("all lifecycle controls should remain disabled after queueing")]
    public async Task ThenLifecycleControlsDisabled()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-install")).ToBeDisabledAsync();
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-rollback")).ToBeDisabledAsync();
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-unload")).ToBeDisabledAsync();
    }

    [Then("the forged lifecycle request should be denied")]
    public void ThenForgedRequestDenied()
    {
        Assert.Contains("Error 401", directResponseBody);
    }

    [Then("the target version warning should be displayed")]
    public async Task ThenTargetVersionWarningDisplayed()
    {
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-warning")).ToContainTextAsync("Target version is required");
    }

    [Then("the lifecycle response should report {string}")]
    public void ThenLifecycleResponseReports(string message)
    {
        using var response = JsonDocument.Parse(directResponseBody ?? throw new InvalidOperationException("Lifecycle response missing."));
        Assert.Equal(message, response.RootElement.GetProperty("message").GetString());
    }

    [Then("the CoD4x lifecycle success toast should be displayed")]
    public async Task ThenSuccessToastDisplayed()
    {
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-success")).ToContainTextAsync("queued successfully");
    }

    [Then("the queued lifecycle request should appear after reload")]
    public async Task ThenQueuedRequestAppearsAfterReload()
    {
        var action = Scenario.GetAttemptedDocument().OperationRequest?.Action.ToString();
        await Assertions.Expect(Browser.Page.GetByTestId("cod4x-pending-request")).ToContainTextAsync(action ?? string.Empty, new() { Timeout = 5000 });
    }

    [Then("the CoD4x lifecycle failure toast should report {string}")]
    public async Task ThenFailureToastReports(string message)
    {
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-error")).ToContainTextAsync(message);
    }

    [Then("no lifecycle request should be queued")]
    public void ThenNoRequestQueued()
    {
        Assert.Empty(Scenario.AttemptedConfigurations);
    }

    [Then("one lifecycle request should have been attempted")]
    public void ThenOneRequestAttempted()
    {
        Assert.Single(Scenario.AttemptedConfigurations);
    }

    [Then("the CoD4x lifecycle browser should report no errors")]
    public void ThenBrowserReportsNoErrors()
    {
        Browser.AssertNoBrowserErrors();
    }

    [AfterScenario]
    public async Task DisposeBrowserAsync()
    {
        if (browser is not null)
            await browser.DisposeAsync();
    }

    private BrowserFixture Browser => browser ?? throw new InvalidOperationException("Browser not started.");
    private Cod4xPluginLifecycleScenario Scenario => scenario ?? throw new InvalidOperationException("Scenario not configured.");
    private string ServerDetailUrl => new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{Scenario.GameServerId}").AbsoluteUri;

    private void Configure(string authenticationProfile)
    {
        profile = authenticationProfile;
        scenario = new Cod4xPluginLifecycleScenario();
    }

    private async Task OpenLifecyclePanelAsync()
    {
        browser ??= await BrowserFixture.CreateAsync(profile ?? throw new InvalidOperationException("Profile missing."), Scenario.ConfigureServices);
        var response = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Browser.Page.Locator("#agentstatus-tab").ClickAsync();
    }

    private async Task ClickLifecycleActionAsync(string testId)
    {
        var responseTask = Browser.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath.Equals("/ServerAdmin/RequestCod4xPluginOperation", StringComparison.Ordinal));
        await Browser.Page.GetByTestId(testId).ClickAsync();
        await responseTask;
    }

    private async Task SubmitDirectlyAsync(string action, string? targetVersion)
    {
        var token = await Browser.Page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        directResponseBody = await Browser.Page.EvaluateAsync<string>(
            "async args => { const response = await fetch(args.url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams(args.fields) }); return await response.text(); }",
            new
            {
                url = new Uri(Browser.Host.BaseAddress, "/ServerAdmin/RequestCod4xPluginOperation").AbsoluteUri,
                fields = new Dictionary<string, string>
                {
                    ["id"] = Scenario.GameServerId.ToString(),
                    ["action"] = action,
                    ["targetVersion"] = targetVersion ?? string.Empty,
                    ["__RequestVerificationToken"] = token,
                },
            });
    }
}
