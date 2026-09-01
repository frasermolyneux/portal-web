using Microsoft.Playwright;
using Reqnroll;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Binding]
public sealed class PlayerModerationSteps
{
    private const string HtmlBearingPlayerName = "<img data-testid='moderation-toast-xss' src='/missing-xss-image' onerror='console.error(\"moderation-toast-xss\")'>";

    private BrowserFixture? browser;
    private string? profile;
    private string? responseBody;

    [Given("a successful player moderation scenario for a direct Kick user")]
    public void GivenSuccessfulDirectKickScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKick, true, true);
    }

    [Given("a successful player moderation scenario for a direct Ban user")]
    public void GivenSuccessfulDirectBanScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerBan, true, true);
    }

    [Given("a successful player moderation scenario for a Kick-only user without admin action creation")]
    public void GivenKickOnlyWithoutAdminActionsScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKickWithoutAdminActions, true, true);
    }

    [Given("a successful player moderation scenario for a game admin")]
    public void GivenSuccessfulGameAdminScenario()
    {
        Configure(TestPrincipalProfiles.GameAdmin, true, true);
    }

    [Given("a successful player moderation scenario for a moderator")]
    public void GivenSuccessfulModeratorScenario()
    {
        Configure(TestPrincipalProfiles.Moderator, true, true);
    }

    [Given("a failing RCON player moderation scenario for a direct Kick user")]
    public void GivenFailingRconScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKick, false, true);
    }

    [Given("a failing persistence player moderation scenario for a direct Kick user")]
    public void GivenFailingPersistenceScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKick, true, false);
    }

    [Given("a mismatched repository player moderation scenario for a direct Kick user")]
    public void GivenMismatchedRepositoryScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKick, true, true, false);
    }

    [Given("a player moderation scenario with an HTML-bearing live player name")]
    public void GivenHtmlBearingPlayerNameScenario()
    {
        Configure(TestPrincipalProfiles.LiveServerKick, true, true, true, HtmlBearingPlayerName);
    }

    [When("the user opens the live player table")]
    public async Task WhenUserOpensPlayerTable()
    {
        await OpenPlayerTableAsync();
    }

    [When("the user kicks the connected player")]
    public async Task WhenUserKicksPlayer()
    {
        await ClickPlayerActionAsync("player-kick", "KickRconPlayer");
    }

    [When("the user applies the {string} action to the connected player")]
    public async Task WhenUserAppliesAction(string action)
    {
        var (testId, endpoint) = action switch
        {
            "TempBan" => ("player-temp-ban", "TempBanRconPlayer"),
            "Ban" => ("player-ban", "BanRconPlayer"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown player action."),
        };
        await ClickPlayerActionAsync(testId, endpoint);
    }

    [When("the user directly submits a forged {string} action")]
    public async Task WhenUserForgesAction(string action)
    {
        await SubmitDirectlyAsync(action, PlayerModerationScenario.PlayerSlot, PlayerModerationScenario.PlayerGuid, PlayerModerationScenario.PlayerName);
    }

    [When("the user directly submits a Kick action with forged player identity")]
    public async Task WhenUserSubmitsForgedIdentity()
    {
        await SubmitDirectlyAsync("KickRconPlayer", PlayerModerationScenario.PlayerSlot, "FORGED-GUID", "Forged Name");
    }

    [When("the user directly submits a Kick action for stale slot 99")]
    public async Task WhenUserSubmitsStaleSlot()
    {
        await SubmitDirectlyAsync("KickRconPlayer", 99, PlayerModerationScenario.PlayerGuid, PlayerModerationScenario.PlayerName);
    }

    [When("the user directly submits a Kick action for the connected player")]
    public async Task WhenUserDirectlyKicksConnectedPlayer()
    {
        await SubmitDirectlyAsync(
            "KickRconPlayer",
            PlayerModerationScenario.PlayerSlot,
            PlayerModerationScenario.PlayerGuid,
            PlayerModerationScenario.PlayerName);
    }

    [Then("only the Kick player control should be available")]
    public async Task ThenOnlyKickAvailable()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("player-kick")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.GetByTestId("player-temp-ban")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("player-ban")).ToHaveCountAsync(0);
    }

    [Then("only the Ban player controls should be available")]
    public async Task ThenOnlyBanAvailable()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("player-kick")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("player-temp-ban")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.GetByTestId("player-ban")).ToHaveCountAsync(1);
    }

    [Then("no player moderation controls should be available")]
    public async Task ThenNoPlayerControlsAvailable()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("player-kick")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("player-temp-ban")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("player-ban")).ToHaveCountAsync(0);
    }

    [Then("the CoD4 {string} command should target slot 7")]
    public void ThenCommandTargetsSlot(string command)
    {
        Assert.Equal($"{command}:7", Assert.Single(Scenario.Commands));
    }

    [Then("a {string} admin action should be recorded for the connected player")]
    public void ThenAdminActionRecorded(string actionType)
    {
        var action = Assert.Single(Scenario.AttemptedAdminActions);
        Assert.Equal(Enum.Parse<AdminActionType>(actionType), action.Type);
        Assert.Equal(Scenario.PlayerId, action.PlayerId);
    }

    [Then("one player admin action should have been attempted")]
    public void ThenOneAdminActionAttempted()
    {
        Assert.Single(Scenario.AttemptedAdminActions);
    }

    [Then("no player moderation command should be recorded")]
    public void ThenNoCommandRecorded()
    {
        Assert.Empty(Scenario.Commands);
    }

    [Then("no player admin action should be recorded")]
    public void ThenNoAdminActionRecorded()
    {
        Assert.Empty(Scenario.AttemptedAdminActions);
    }

    [Then("the player moderation response should report {string}")]
    public void ThenResponseReports(string message)
    {
        using var response = JsonDocument.Parse(responseBody ?? throw new InvalidOperationException("Response body missing."));
        Assert.Equal(message, response.RootElement.GetProperty("message").GetString());
    }

    [Then("the player moderation success toast should be displayed")]
    public async Task ThenSuccessToastDisplayed()
    {
        var toast = Browser.Page.Locator("#toast-container .toast-success, #toast-container .toast-warning");
        await Assertions.Expect(toast).ToBeVisibleAsync();
    }

    [Then("the player moderation failure toast should report {string}")]
    public async Task ThenFailureToastReports(string message)
    {
        var toast = Browser.Page.Locator("#toast-container .toast-error");
        await Assertions.Expect(toast).ToBeVisibleAsync();
        await Assertions.Expect(toast).ToContainTextAsync(message);
    }

    [Then("the player moderation toast should contain the live player name as text")]
    public async Task ThenToastContainsLivePlayerNameAsText()
    {
        var toast = Browser.Page.Locator("#toast-container .toast-success");
        await Assertions.Expect(toast).ToBeVisibleAsync();
        await Assertions.Expect(toast).ToContainTextAsync(HtmlBearingPlayerName);
    }

    [Then("the player moderation toast should contain no injected image")]
    public async Task ThenToastContainsNoInjectedImage()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("moderation-toast-xss")).ToHaveCountAsync(0);
    }

    [Then("the player moderation browser should report no errors")]
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
    private PlayerModerationScenario Scenario { get => field ?? throw new InvalidOperationException("Scenario not configured."); set; }
    private string ServerDetailUrl => new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{Scenario.GameServerId}").AbsoluteUri;

    private void Configure(
        string authenticationProfile,
        bool rconSucceeds,
        bool persistenceSucceeds,
        bool repositoryPlayerMatches = true,
        string playerName = PlayerModerationScenario.PlayerName)
    {
        profile = authenticationProfile;
        Scenario = new PlayerModerationScenario(rconSucceeds, persistenceSucceeds, repositoryPlayerMatches, playerName);
    }

    private async Task OpenPlayerTableAsync()
    {
        await StartBrowserAsync();
        var response = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Assertions.Expect(Browser.Page.Locator("#sd-playersTable tbody tr")).ToHaveCountAsync(1);
    }

    private async Task StartBrowserAsync()
    {
        browser ??= await BrowserFixture.CreateAsync(profile ?? throw new InvalidOperationException("Profile missing."), Scenario.ConfigureServices);
    }

    private async Task ClickPlayerActionAsync(string testId, string action)
    {
        await OpenPlayerTableAsync();
        Browser.Page.Dialog += AcceptDialog;
        try
        {
            var responseTask = Browser.Page.WaitForResponseAsync(candidate =>
                candidate.Request.Method == "POST" &&
                new Uri(candidate.Url).AbsolutePath.Equals($"/ServerAdmin/{action}/{Scenario.GameServerId}", StringComparison.Ordinal));
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

    private async Task SubmitDirectlyAsync(string action, int playerSlot, string playerGuid, string playerName)
    {
        await OpenPlayerTableAsync();
        var token = await Browser.Page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
        Assert.False(string.IsNullOrWhiteSpace(token));
        responseBody = await Browser.Page.EvaluateAsync<string>(
            "async args => { const response = await fetch(args.url, { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams(args.fields) }); return await response.text(); }",
            new
            {
                url = new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/{action}/{Scenario.GameServerId}").AbsoluteUri,
                fields = new Dictionary<string, string>
                {
                    ["playerSlot"] = playerSlot.ToString(),
                    ["playerGuid"] = playerGuid,
                    ["playerName"] = playerName,
                    ["__RequestVerificationToken"] = token,
                },
            });
    }
}
