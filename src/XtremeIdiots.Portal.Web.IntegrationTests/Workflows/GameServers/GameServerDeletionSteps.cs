using Microsoft.Playwright;
using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Binding]
public sealed class GameServerDeletionSteps
{
    private BrowserFixture? browser;
    private string? profile;
    private IResponse? response;

    [Given("a successful game server deletion scenario for a senior admin")]
    public void GivenASuccessfulGameServerDeletionScenarioForASeniorAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.SeniorAdmin, deleteSucceeds: true);
    }

    [Given("a successful game server deletion scenario for a head admin")]
    public void GivenASuccessfulGameServerDeletionScenarioForAHeadAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.HeadAdmin, deleteSucceeds: true);
    }

    [Given("a failing game server deletion scenario for a senior admin")]
    public void GivenAFailingGameServerDeletionScenarioForASeniorAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.SeniorAdmin, deleteSucceeds: false);
    }

    [When("the senior admin confirms game server deletion")]
    public async Task WhenTheSeniorAdminConfirmsGameServerDeletion()
    {
        await OpenDeleteFormAsync();
        response = await Browser.Page.RunAndWaitForResponseAsync(
            () => Browser.Page.GetByTestId("game-server-delete-submit").ClickAsync(),
            browserResponse => browserResponse.Request.Method == "POST" &&
                new Uri(browserResponse.Url).AbsolutePath == new Uri(DeleteUrl).AbsolutePath);
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/GameServers");
    }

    [When("the head admin navigates directly to game server deletion")]
    public async Task WhenTheHeadAdminNavigatesDirectlyToGameServerDeletion()
    {
        await StartBrowserAsync();
        response = await Browser.Page.GotoAsync(DeleteUrl);
    }

    [Then("the delete command should contain the game server identifier")]
    [Then("the failed delete command should contain the game server identifier")]
    public void ThenTheDeleteCommandShouldContainTheGameServerIdentifier()
    {
        Assert.Equal(Scenario.GameServerId, Assert.Single(Scenario.DeletedGameServerIds));
    }

    [Then("successful game server deletion feedback should be displayed")]
    public async Task ThenSuccessfulGameServerDeletionFeedbackShouldBeDisplayed()
    {
        await Assertions.Expect(Browser.Page.GetByText(
            "The game server CoD4 Server To Delete has been deleted for CallOfDuty4")).ToBeVisibleAsync();
    }

    [Then("failed game server deletion feedback should be displayed")]
    public async Task ThenFailedGameServerDeletionFeedbackShouldBeDisplayed()
    {
        await Assertions.Expect(Browser.Page.GetByText("Failed to delete the game server. Please try again.")).ToBeVisibleAsync();
    }

    [Then("game server deletion should be denied")]
    public async Task ThenGameServerDeletionShouldBeDenied()
    {
        Assert.NotNull(response);
        Assert.EndsWith("/Errors/Display/401", Browser.Page.Url, StringComparison.Ordinal);
        await Assertions.Expect(Browser.Page.GetByTestId("game-server-delete-form")).ToHaveCountAsync(0);
    }

    [Then("no game server delete command should be recorded")]
    public void ThenNoGameServerDeleteCommandShouldBeRecorded()
    {
        Assert.Empty(Scenario.DeletedGameServerIds);
    }

    [Then("the game server deletion browser should report no errors")]
    public void ThenTheGameServerDeletionBrowserShouldReportNoErrors()
    {
        Browser.AssertNoBrowserErrors();
    }

    [AfterScenario]
    public async Task DisposeBrowserAsync()
    {
        if (browser is not null)
            await browser.DisposeAsync();
    }

    private BrowserFixture Browser => browser ?? throw new InvalidOperationException("The browser has not been started.");

    private string DeleteUrl => new Uri(Browser.Host.BaseAddress, $"/GameServers/Delete/{Scenario.GameServerId}").AbsoluteUri;

    private GameServerDeletionScenario Scenario { get => field ?? throw new InvalidOperationException("The game server deletion scenario has not been configured."); set; }

    private void ConfigureScenario(string authenticationProfile, bool deleteSucceeds)
    {
        profile = authenticationProfile;
        Scenario = new GameServerDeletionScenario(deleteSucceeds);
    }

    private async Task OpenDeleteFormAsync()
    {
        await StartBrowserAsync();
        var formResponse = await Browser.Page.GotoAsync(DeleteUrl);
        Assert.NotNull(formResponse);
        Assert.True(formResponse.Ok);
        await Assertions.Expect(Browser.Page.GetByTestId("game-server-delete-form")).ToBeVisibleAsync();
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(
            profile ?? throw new InvalidOperationException("The authentication profile has not been configured."),
            Scenario.ConfigureServices);
    }
}
