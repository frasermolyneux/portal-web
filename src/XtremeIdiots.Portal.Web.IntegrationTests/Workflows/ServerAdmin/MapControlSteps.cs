using Microsoft.Playwright;
using Reqnroll;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Binding]
public sealed class MapControlSteps
{
    private BrowserFixture? browser;
    private string? profile;
    private string? responseBody;
    private int? responseStatus;
    private MapControlScenario? scenario;

    [Given("a successful map control scenario for a direct map user")]
    public void GivenSuccessfulDirectMapScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerMap, true);
    }

    [Given("a successful map control scenario for a game admin")]
    public void GivenSuccessfulGameAdminScenario()
    {
        Configure(TestPrincipalProfiles.GameAdmin, true);
    }

    [Given("a successful map control scenario for a head admin")]
    public void GivenSuccessfulHeadAdminScenario()
    {
        Configure(TestPrincipalProfiles.HeadAdmin, true);
    }

    [Given("a successful map control scenario for a direct restart user")]
    public void GivenSuccessfulDirectRestartScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerRestart, true);
    }

    [Given("a failing map control scenario for a game admin")]
    public void GivenFailingGameAdminScenario()
    {
        Configure(TestPrincipalProfiles.GameAdmin, false);
    }

    [When("the user directly loads map {string}")]
    public async Task WhenUserDirectlyLoadsMap(string mapName)
    {
        await SubmitDirectlyAsync("LoadMap", new() { ["mapName"] = mapName });
    }

    [When("the user directly loads a whitespace-only map")]
    public async Task WhenUserDirectlyLoadsWhitespaceMap()
    {
        await SubmitDirectlyAsync("LoadMap", new() { ["mapName"] = "   " });
    }

    [When("the user sends the {string} map command")]
    public async Task WhenUserSendsMapCommand(string command)
    {
        await OpenMapControlAsync();
        var testId = command switch
        {
            "restart" => "map-restart",
            "fast restart" => "map-fast-restart",
            "next" => "map-next",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown map command."),
        };
        var action = command switch
        {
            "restart" => "RestartMap",
            "fast restart" => "FastRestartMap",
            "next" => "NextMap",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown map command."),
        };
        await ClickConfirmedCommandAsync(testId, action);
    }

    [When("the user opens map control")]
    public async Task WhenUserOpensMapControl()
    {
        await OpenMapControlAsync();
    }

    [When("the user sends the server restart command")]
    public async Task WhenUserRestartsServer()
    {
        await OpenServerDetailAsync();
        await ClickConfirmedCommandAsync("server-restart", "RestartServer");
    }

    [When("the user directly submits a forged server restart")]
    public async Task WhenUserForgesServerRestart()
    {
        await SubmitDirectlyAsync("RestartServer");
    }

    [Then("the CoD4 map command should contain {string}")]
    public void ThenMapCommandContains(string mapName)
    {
        Assert.Equal($"map:{mapName}", Assert.Single(Scenario.Commands));
    }

    [Then("the map response should report {string}")]
    public void ThenMapResponseReports(string message)
    {
        Assert.Equal(200, responseStatus);
        using var response = JsonDocument.Parse(responseBody ?? throw new InvalidOperationException("Response body missing."));
        Assert.Equal(message, response.RootElement.GetProperty("message").GetString());
    }

    [Then("the {string} map control command should be recorded")]
    public void ThenMapCommandRecorded(string command)
    {
        Assert.Equal(command, Assert.Single(Scenario.Commands));
    }

    [Then("the server restart command should be recorded")]
    public void ThenServerRestartRecorded()
    {
        Assert.Equal("server restart", Assert.Single(Scenario.Commands));
    }

    [Then("no map control command should be recorded")]
    public void ThenNoCommandRecorded()
    {
        Assert.Empty(Scenario.Commands);
    }

    [Then("the map command controls should be available")]
    public async Task ThenMapControlsAvailable()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("map-restart")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.GetByTestId("map-fast-restart")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.GetByTestId("map-next")).ToHaveCountAsync(1);
    }

    [Then("the server restart control should not be present")]
    public async Task ThenServerRestartNotPresent()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("server-restart")).ToHaveCountAsync(0);
    }

    [Then("the map command controls should not be present")]
    public async Task ThenMapControlsNotPresent()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("map-restart")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("map-fast-restart")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("map-next")).ToHaveCountAsync(0);
    }

    [Then("the forged map control command should be denied")]
    public void ThenForgedCommandDenied()
    {
        Assert.Equal(200, responseStatus);
        Assert.Contains("Error 401", responseBody);
    }

    [Then("the map command success toast should be displayed")]
    public async Task ThenSuccessToastDisplayed()
    {
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
    }

    [Then("the map command failure toast should report {string}")]
    public async Task ThenFailureToastReports(string message)
    {
        var toast = Browser.Page.Locator("#toast-container .toast-error");
        await Assertions.Expect(toast).ToBeVisibleAsync();
        await Assertions.Expect(toast).ToContainTextAsync(message);
    }

    [Then("the map control browser should report no errors")]
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
    private MapControlScenario Scenario => scenario ?? throw new InvalidOperationException("Scenario not configured.");
    private string ServerDetailUrl => new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{Scenario.GameServerId}").AbsoluteUri;

    private void Configure(string authenticationProfile, bool commandsSucceed)
    {
        profile = authenticationProfile;
        scenario = new MapControlScenario(commandsSucceed);
    }

    private async Task OpenServerDetailAsync()
    {
        await StartBrowserAsync();
        var response = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok);
    }

    private async Task OpenMapControlAsync()
    {
        await OpenServerDetailAsync();
        await Browser.Page.Locator("#mapcontrol-tab").ClickAsync();
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(profile ?? throw new InvalidOperationException("Profile missing."), Scenario.ConfigureServices);
    }

    private async Task ClickConfirmedCommandAsync(string testId, string action)
    {
        Browser.Page.Dialog += AcceptDialog;
        try
        {
            var responseTask = Browser.Page.WaitForResponseAsync(candidate =>
                candidate.Request.Method == "POST" &&
                new Uri(candidate.Url).AbsolutePath.Equals(
                    $"/ServerAdmin/{action}/{Scenario.GameServerId}",
                    StringComparison.Ordinal));
            await Browser.Page.GetByTestId(testId).ClickAsync();
            await responseTask;
        }
        finally
        {
            Browser.Page.Dialog -= AcceptDialog;
        }
    }

    private static void AcceptDialog(object? sender, IDialog dialog)
    {
        _ = dialog.AcceptAsync();
    }

    private async Task SubmitDirectlyAsync(string action, Dictionary<string, string>? fields = null)
    {
        await OpenServerDetailAsync();
        var token = await Browser.Page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        fields ??= [];
        fields["__RequestVerificationToken"] = token;
        var result = await Browser.Page.EvaluateAsync<DirectResponse>(
            "async args => { const response = await fetch(args.url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams(args.fields) }); return { status: response.status, body: await response.text() }; }",
            new { url = new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/{action}/{Scenario.GameServerId}").AbsoluteUri, fields });
        responseStatus = result.Status;
        responseBody = result.Body;
    }

    private sealed class DirectResponse
    {
        public string Body { get; set; } = string.Empty;
        public int Status { get; set; }
    }
}
