using Microsoft.Playwright;
using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Binding]
public sealed class SayCommandSteps
{
    private BrowserFixture? browser;
    private string? profile;
    private string? responseBody;
    private int? responseStatus;
    private SayCommandScenario? scenario;

    [Given("a successful Say command scenario for a direct-permission user")]
    public void GivenASuccessfulSayScenarioForDirectUser()
    {
        Configure(TestPrincipalProfiles.LiveServerSay, true);
    }

    [Given("a successful Say command scenario for a game admin")]
    public void GivenASuccessfulSayScenarioForGameAdmin()
    {
        Configure(TestPrincipalProfiles.GameAdmin, true);
    }

    [Given("a successful Say command scenario for a moderator")]
    public void GivenASuccessfulSayScenarioForModerator()
    {
        Configure(TestPrincipalProfiles.Moderator, true);
    }

    [Given("a failing Say command scenario for a direct-permission user")]
    public void GivenAFailingSayScenarioForDirectUser()
    {
        Configure(TestPrincipalProfiles.LiveServerSay, false);
    }

    [When("the user broadcasts {string}")]
    public async Task WhenTheUserBroadcasts(string message)
    {
        await OpenServerDetailAsync();
        await Browser.Page.GetByTestId("server-say-message").FillAsync(message);
        var responseTask = WaitForSayResponseAsync();
        await Browser.Page.GetByTestId("server-say-submit").ClickAsync();
        var sayResponse = await responseTask;
        responseStatus = sayResponse.Status;
        responseBody = await sayResponse.TextAsync();
    }

    [When("the user directly submits a whitespace-only Say message")]
    public async Task WhenTheUserDirectlySubmitsWhitespaceSay()
    {
        await OpenServerDetailAsync();
        await SubmitSayDirectlyAsync("   ");
    }

    [When("the moderator opens the server detail page")]
    public async Task WhenTheModeratorOpensServerDetail()
    {
        await StartBrowserAsync();
        var pageResponse = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(pageResponse);
        responseStatus = pageResponse.Status;
    }

    [When("the moderator directly submits a forged Say message")]
    public async Task WhenTheModeratorSubmitsForgedSay()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(ServerDetailUrl);
        await SubmitSayDirectlyAsync("Forged broadcast");
    }

    [Then("the CoD4 Say command should contain {string}")]
    public void ThenSayCommandContains(string message)
    {
        Assert.Equal(message, Assert.Single(Scenario.Messages));
    }

    [Then("the Say success toast should be displayed")]
    public async Task ThenSuccessToastIsDisplayed()
    {
        Assert.Equal(200, responseStatus);
        Assert.Contains("\"success\":true", responseBody, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-success")).ToBeVisibleAsync();
    }

    [Then("the Say failure toast should be displayed")]
    public async Task ThenFailureToastIsDisplayed()
    {
        Assert.Equal(200, responseStatus);
        Assert.Contains("Failed to send message to server", responseBody);
        await Assertions.Expect(Browser.Page.Locator("#toast-container .toast-error")).ToBeVisibleAsync();
    }

    [Then("the Say message field should be cleared")]
    public async Task ThenMessageFieldIsCleared()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("server-say-message")).ToHaveValueAsync(string.Empty);
    }

    [Then("the Say message field should retain {string}")]
    public async Task ThenMessageFieldIsRetained(string message)
    {
        await Assertions.Expect(Browser.Page.GetByTestId("server-say-message")).ToHaveValueAsync(message);
    }

    [Then("the Say validation response should report {string}")]
    public void ThenValidationResponseReports(string message)
    {
        Assert.Equal(200, responseStatus);
        Assert.Contains(message, responseBody);
    }

    [Then("the Say form should not be visible")]
    public async Task ThenSayFormIsNotVisible()
    {
        Assert.Equal(200, responseStatus);
        await Assertions.Expect(Browser.Page.GetByTestId("server-say-form")).ToBeHiddenAsync();
    }

    [Then("the forged Say command should be denied")]
    public void ThenForgedSayIsDenied()
    {
        Assert.Equal(200, responseStatus);
        Assert.Contains("Error 401", responseBody);
    }

    [Then("no Say command should be recorded")]
    public void ThenNoSayCommandIsRecorded()
    {
        Assert.Empty(Scenario.Messages);
    }

    [Then("the Say browser should report no errors")]
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
    private SayCommandScenario Scenario => scenario ?? throw new InvalidOperationException("Scenario not configured.");
    private string ServerDetailUrl => new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{Scenario.GameServerId}").AbsoluteUri;

    private void Configure(string authenticationProfile, bool saySucceeds)
    {
        profile = authenticationProfile;
        scenario = new SayCommandScenario(saySucceeds);
    }

    private async Task OpenServerDetailAsync()
    {
        await StartBrowserAsync();
        var pageResponse = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(pageResponse);
        Assert.True(pageResponse.Ok);
        await Assertions.Expect(Browser.Page.GetByTestId("server-say-form")).ToBeVisibleAsync();
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(
            profile ?? throw new InvalidOperationException("Profile missing."),
            Scenario.ConfigureServices);
    }

    private Task<IResponse> WaitForSayResponseAsync()
    {
        return Browser.Page.WaitForResponseAsync(candidate =>
            candidate.Request.Method == "POST" &&
            new Uri(candidate.Url).AbsolutePath.Equals(
                $"/ServerAdmin/SendSayCommand/{Scenario.GameServerId}",
                StringComparison.Ordinal));
    }

    private async Task SubmitSayDirectlyAsync(string message)
    {
        var token = await Browser.Page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        var result = await Browser.Page.EvaluateAsync<DirectResponse>(
            "async args => { const body = new URLSearchParams({ message: args.message, __RequestVerificationToken: args.token }); const response = await fetch(args.url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body }); return { status: response.status, body: await response.text() }; }",
            new
            {
                url = new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/SendSayCommand/{Scenario.GameServerId}").AbsoluteUri,
                message,
                token,
            });
        responseStatus = result.Status;
        responseBody = result.Body;
    }

    private sealed class DirectResponse
    {
        public string Body { get; set; } = string.Empty;

        public int Status { get; set; }
    }
}
