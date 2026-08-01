using Reqnroll;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Binding]
public sealed class RconCredentialsSteps
{
    private BrowserFixture? browser;
    private bool editFormAvailableWithoutRconControls;
    private string? profile;
    private Microsoft.Playwright.IResponse? response;
    private GameServerRconScenario? scenario;

    [Given("a successful RCON credential scenario for a head admin")]
    public void GivenASuccessfulRconCredentialScenarioForAHeadAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.HeadAdmin, rconUpsertSucceeds: true);
    }

    [Given("a successful RCON credential scenario for a game admin")]
    public void GivenASuccessfulRconCredentialScenarioForAGameAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.GameAdmin, rconUpsertSucceeds: true);
    }

    [Given("a successful RCON credential scenario for a core server writer without RCON permission")]
    public void GivenASuccessfulRconCredentialScenarioForACoreServerWriterWithoutRconPermission()
    {
        ConfigureScenario(TestPrincipalProfiles.GameServerWriterWithoutRcon, rconUpsertSucceeds: true);
    }

    [Given("a failing RCON repository scenario for a head admin")]
    public void GivenAFailingRconRepositoryScenarioForAHeadAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.HeadAdmin, rconUpsertSucceeds: false);
    }

    [When("the head admin changes the RCON password to {string}")]
    public async Task WhenTheHeadAdminChangesTheRconPasswordTo(string password)
    {
        await OpenEditFormAsync();
        await Browser.Page.GetByTestId("rcon-password-input").FillAsync(password);
        response = await SubmitEditFormAsync();
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/GameServers");
    }

    [When("the head admin toggles RCON password visibility")]
    public async Task WhenTheHeadAdminTogglesRconPasswordVisibility()
    {
        if (browser is null)
            await OpenEditFormAsync();

        await Browser.Page.GetByTestId("rcon-password-toggle").ClickAsync();
    }

    [When("the head admin clears and saves the RCON password")]
    public async Task WhenTheHeadAdminClearsAndSavesTheRconPassword()
    {
        await OpenEditFormAsync();
        await Browser.Page.GetByTestId("rcon-password-input").FillAsync(string.Empty);
        response = await SubmitEditFormAsync();
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/GameServers");
    }

    [When("the head admin submits the edit form without a server title")]
    public async Task WhenTheHeadAdminSubmitsTheEditFormWithoutAServerTitle()
    {
        await OpenEditFormAsync();
        await Browser.Page.Locator("#general-tab-btn").ClickAsync();
        var title = Browser.Page.Locator("input[name='GameServer.Title']");
        await title.FillAsync(string.Empty);
        var responseTask = Browser.Page.WaitForResponseAsync(browserResponse =>
            browserResponse.Request.Method == "POST" &&
            new Uri(browserResponse.Url).AbsolutePath.StartsWith("/GameServers/Edit", StringComparison.Ordinal));
        await Browser.Page.Locator("#game-server-edit-form").EvaluateAsync("form => form.submit()");
        response = await responseTask;
    }

    [When("the game admin navigates directly to the server edit form")]
    public async Task WhenTheGameAdminNavigatesDirectlyToTheServerEditForm()
    {
        await StartBrowserAsync();
        response = await Browser.Page.GotoAsync(EditUrl);
    }

    [When("the core server writer opens the edit form and forges an RCON password")]
    public async Task WhenTheCoreServerWriterOpensTheEditFormAndForgesAnRconPassword()
    {
        await StartBrowserAsync();
        var formResponse = await Browser.Page.GotoAsync(EditUrl);
        Assert.NotNull(formResponse);
        Assert.True(formResponse.Ok);
        editFormAvailableWithoutRconControls =
            await Browser.Page.Locator("#game-server-edit-form").IsVisibleAsync() &&
            !await Browser.Page.Locator("#rcon-tab-btn").IsVisibleAsync() &&
            !await Browser.Page.GetByTestId("rcon-password-input").IsVisibleAsync();
        await Browser.Page.Locator("#game-server-edit-form").EvaluateAsync(
            "form => { const input = document.createElement('input'); input.name = 'RconConfigPassword'; input.value = 'ForgedPassword'; form.appendChild(input); }");
        response = await SubmitEditFormAsync();
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/GameServers");
    }

    [Then("the game server update should preserve the core server details")]
    public void ThenTheGameServerUpdateShouldPreserveTheCoreServerDetails()
    {
        var command = Assert.Single(Scenario.UpdatedGameServers);
        Assert.Equal(Scenario.GameServerId, command.GameServerId);
        Assert.Equal("CoD4 Server 1", command.Title);
        Assert.Equal("127.0.0.1", command.Hostname);
        Assert.Equal(28960, command.QueryPort);
        Assert.Equal(GameServerPlatform.Windows, command.Platform);
        Assert.False(command.AgentEnabled);
        Assert.False(command.FileTransportEnabled);
        Assert.Equal(FileTransportType.Unknown, command.FileTransportType);
        Assert.True(command.RconEnabled);
        Assert.False(command.BanFileSyncEnabled);
        Assert.Equal("/", command.BanFileRootPath);
        Assert.False(command.ServerListEnabled);
    }

    [Then("the game server update should have been recorded")]
    public void ThenTheGameServerUpdateShouldHaveBeenRecorded()
    {
        Assert.Single(Scenario.UpdatedGameServers);
    }

    [Then("the RCON configuration should contain password {string}")]
    public void ThenTheRconConfigurationShouldContainPassword(string password)
    {
        AssertRconPassword(password);
    }

    [Then("the failed RCON configuration should contain password {string}")]
    public void ThenTheFailedRconConfigurationShouldContainPassword(string password)
    {
        AssertRconPassword(password);
    }

    [Then("successful game server update feedback should be displayed")]
    public async Task ThenSuccessfulGameServerUpdateFeedbackShouldBeDisplayed()
    {
        Assert.True(await Browser.Page.GetByText("The game server CoD4 Server 1 has been updated for CallOfDuty4").IsVisibleAsync());
    }

    [Then("the RCON configuration failure warning should be displayed")]
    public async Task ThenTheRconConfigurationFailureWarningShouldBeDisplayed()
    {
        Assert.True(await Browser.Page.GetByText(
            "The game server CoD4 Server 1 has been updated but some configuration sections failed to save: rcon").IsVisibleAsync());
    }

    [Then("the RCON password should be visible")]
    public async Task ThenTheRconPasswordShouldBeVisible()
    {
        Assert.Equal("text", await Browser.Page.GetByTestId("rcon-password-input").GetAttributeAsync("type"));
    }

    [Then("the RCON password should be hidden")]
    public async Task ThenTheRconPasswordShouldBeHidden()
    {
        Assert.Equal("password", await Browser.Page.GetByTestId("rcon-password-input").GetAttributeAsync("type"));
    }

    [Then("the required server title validation should be displayed")]
    public async Task ThenTheRequiredServerTitleValidationShouldBeDisplayed()
    {
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Equal(
            "The Title field is required.",
            await Browser.Page.Locator("span[data-valmsg-for='GameServer.Title']").TextContentAsync());
    }

    [Then("game server editing should be denied")]
    public async Task ThenGameServerEditingShouldBeDenied()
    {
        Assert.NotNull(response);
        Assert.Equal(403, response.Status);
        Assert.False(await Browser.Page.Locator("#game-server-edit-form").IsVisibleAsync());
    }

    [Then("the server edit form should remain available without RCON controls")]
    public void ThenTheServerEditFormShouldRemainAvailableWithoutRconControls()
    {
        Assert.True(editFormAvailableWithoutRconControls);
    }

    [Then("the core game server update should be recorded without an RCON write")]
    public void ThenTheCoreGameServerUpdateShouldBeRecordedWithoutAnRconWrite()
    {
        Assert.Single(Scenario.UpdatedGameServers);
        Assert.Empty(Scenario.UpsertedConfigurations);
    }

    [Then("no game server or RCON writes should be recorded")]
    public void ThenNoGameServerOrRconWritesShouldBeRecorded()
    {
        Assert.Empty(Scenario.UpdatedGameServers);
        Assert.Empty(Scenario.UpsertedConfigurations);
    }

    [Then("the RCON browser should report no errors")]
    public void ThenTheRconBrowserShouldReportNoErrors()
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

    private string EditUrl => new Uri(Browser.Host.BaseAddress, $"/GameServers/Edit/{Scenario.GameServerId}").AbsoluteUri;

    private GameServerRconScenario Scenario => scenario ?? throw new InvalidOperationException("The RCON scenario has not been configured.");

    private void AssertRconPassword(string password)
    {
        var command = Assert.Single(Scenario.UpsertedConfigurations);
        Assert.Equal("rcon", command.Namespace);
        using var document = JsonDocument.Parse(command.Configuration);
        Assert.Equal(password, document.RootElement.GetProperty("password").GetString());
    }

    private void ConfigureScenario(string authenticationProfile, bool rconUpsertSucceeds)
    {
        profile = authenticationProfile;
        scenario = new GameServerRconScenario(rconUpsertSucceeds);
    }

    private async Task OpenEditFormAsync()
    {
        await StartBrowserAsync();
        var formResponse = await Browser.Page.GotoAsync(EditUrl);
        Assert.NotNull(formResponse);
        Assert.True(formResponse.Ok);
        Assert.True(await Browser.Page.Locator("#game-server-edit-form").IsVisibleAsync());
        await Browser.Page.Locator("#rcon-tab-btn").ClickAsync();
        Assert.True(await Browser.Page.GetByTestId("rcon-password-input").IsVisibleAsync());
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(
            profile ?? throw new InvalidOperationException("The authentication profile has not been configured."),
            Scenario.ConfigureServices);
    }

    private async Task<Microsoft.Playwright.IResponse> SubmitEditFormAsync()
    {
        var responseTask = Browser.Page.WaitForResponseAsync(browserResponse =>
            browserResponse.Request.Method == "POST" &&
            new Uri(browserResponse.Url).AbsolutePath.StartsWith("/GameServers/Edit", StringComparison.Ordinal));
        await Browser.Page.GetByTestId("game-server-save").ClickAsync();
        return await responseTask;
    }
}
