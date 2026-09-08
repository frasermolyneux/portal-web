using Microsoft.Playwright;
using Reqnroll;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Binding]
public sealed class FileTransportCredentialsSteps
{
    private BrowserFixture? browser;
    private bool formWithoutCredentialsVerified;
    private string? profile;
    private IResponse? response;

    [Given("a successful file transport scenario for a head admin")]
    public void GivenASuccessfulFileTransportScenarioForAHeadAdmin()
    {
        Configure(TestPrincipalProfiles.HeadAdmin, true);
    }

    [Given("a successful file transport scenario for a core server writer without credential permission")]
    public void GivenASuccessfulFileTransportScenarioForACoreWriter()
    {
        Configure(TestPrincipalProfiles.GameServerWriterWithoutRcon, true);
    }

    [Given("a failing file transport repository scenario for a head admin")]
    public void GivenAFailingFileTransportRepositoryScenarioForAHeadAdmin()
    {
        Configure(TestPrincipalProfiles.HeadAdmin, false);
    }

    [Given("a file transport scenario with no existing SFTP fingerprint")]
    public void GivenAFileTransportScenarioWithNoExistingSftpFingerprint()
    {
        profile = TestPrincipalProfiles.HeadAdmin;
        Scenario = new FileTransportScenario(existingFingerprint: false);
    }

    [When("the head admin updates all SFTP connection fields")]
    public async Task WhenTheHeadAdminUpdatesAllSftpConnectionFields()
    {
        await OpenFileTransportTabAsync();
        await FillConnectionAsync("new-sftp.example.com", "2222", "new-user", "NewSftpPassword", "11:22:33", "/new-maps");
        await SubmitAndFollowAsync();
    }

    [When("the head admin switches the transport type to FTP")]
    public async Task WhenTheHeadAdminSwitchesTheTransportTypeToFtp()
    {
        await OpenFileTransportTabAsync();
        await Browser.Page.GetByTestId("file-transport-type").SelectOptionAsync("Ftp");
    }

    [When("the head admin clears and saves the SFTP password and fingerprint")]
    public async Task WhenTheHeadAdminClearsAndSavesSftpSecrets()
    {
        await OpenFileTransportTabAsync();
        await Browser.Page.GetByTestId("file-transport-password").FillAsync(string.Empty);
        await Browser.Page.GetByTestId("sftp-host-key-fingerprint").FillAsync(string.Empty);
        await SubmitAndFollowAsync();
    }

    [When("the head admin submits SFTP without a host key fingerprint")]
    public async Task WhenTheHeadAdminSubmitsSftpWithoutFingerprint()
    {
        await OpenFileTransportTabAsync();
        await Browser.Page.GetByTestId("sftp-host-key-fingerprint").FillAsync(string.Empty);
        await Browser.Page.GetByTestId("file-transport-password").FillAsync("ChangedPassword");
        response = await NativeSubmitAsync();
    }

    [When("the head admin submits a maps root containing path traversal")]
    public async Task WhenTheHeadAdminSubmitsTraversalMapsRoot()
    {
        await OpenFileTransportTabAsync();
        await Browser.Page.GetByTestId("file-transport-maps-root").FillAsync("/maps/../secrets");
        response = await NativeSubmitAsync();
    }

    [When("the core server writer opens the edit form and forges file transport credentials")]
    public async Task WhenTheCoreWriterForgesCredentials()
    {
        await StartBrowserAsync();
        var formResponse = await Browser.Page.GotoAsync(EditUrl);
        Assert.NotNull(formResponse);
        Assert.True(formResponse.Ok);
        // Verify before submitting: the successful POST navigates away from this form.
        await Assertions.Expect(Browser.Page.Locator("#game-server-edit-form")).ToBeVisibleAsync();
        await Assertions.Expect(Browser.Page.Locator("#filetransfer-tab-btn")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("file-transport-password")).ToHaveCountAsync(0);
        formWithoutCredentialsVerified = true;
        await Browser.Page.Locator("#game-server-edit-form").EvaluateAsync(
            "form => { const values = { 'FtpConfigPassword': 'ForgedPassword', 'GameServer.FileTransportEnabled': 'false', 'GameServer.FileTransportType': 'Ftp' }; Object.entries(values).forEach(([name, value]) => { const input = document.createElement('input'); input.name = name; input.value = value; form.appendChild(input); }); }");
        await SubmitAndFollowAsync();
    }

    [When("the head admin toggles file transport password visibility")]
    public async Task WhenTheHeadAdminTogglesPasswordVisibility()
    {
        if (browser is null)
            await OpenFileTransportTabAsync();
        await Browser.Page.GetByTestId("file-transport-password-toggle").ClickAsync();
    }

    [Then("the SFTP configuration should contain the updated connection details")]
    [Then("the failed SFTP configuration should contain the updated connection details")]
    public void ThenSftpConfigurationContainsUpdatedDetails()
    {
        AssertTransport("sftp", "new-sftp.example.com", 2222, "new-user", "NewSftpPassword", "/new-maps", "11:22:33");
    }

    [Then("the file transport server update should preserve enabled SFTP state")]
    public void ThenServerUpdatePreservesSftpState()
    {
        var command = Assert.Single(Scenario.UpdatedGameServers);
        Assert.True(command.FileTransportEnabled);
        Assert.Equal(FileTransportType.Sftp, command.FileTransportType);
    }

    [Then("the file transport label and default port should show FTP values")]
    public async Task ThenFtpUiValuesAreDisplayed()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("file-transport-port")).ToHaveValueAsync("21");
        await Assertions.Expect(Browser.Page.Locator(".js-file-transport-label")).ToHaveTextAsync(["FTP", "FTP", "FTP", "FTP"]);
    }

    [Then("the SFTP fingerprint control should be hidden")]
    public async Task ThenFingerprintIsHidden()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("sftp-host-key-fingerprint")).ToBeHiddenAsync();
    }

    [Then("the SFTP configuration should preserve the current password and fingerprint")]
    public void ThenSftpSecretsArePreserved()
    {
        AssertTransport("sftp", "sftp.example.com", 22, "ops-user", "CurrentSftpPassword", "/maps", "aa:bb:cc");
    }

    [Then("the SFTP fingerprint validation should be displayed")]
    public async Task ThenFingerprintValidationIsDisplayed()
    {
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await Assertions.Expect(Browser.Page.Locator("body")).ToContainTextAsync("SFTP host key fingerprint is required");
    }

    [Then("the maps root validation should be displayed")]
    public async Task ThenMapsRootValidationIsDisplayed()
    {
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await Assertions.Expect(Browser.Page.Locator("body")).ToContainTextAsync("Maps root path cannot contain path traversal segments");
    }

    [Then("no file transport writes should be recorded")]
    public void ThenNoWritesAreRecorded()
    {
        Assert.Empty(Scenario.UpdatedGameServers);
        Assert.Empty(Scenario.UpsertedConfigurations);
    }

    [Then("the edit form should remain available without file transport controls")]
    public void ThenFormIsAvailableWithoutCredentials()
    {
        Assert.True(formWithoutCredentialsVerified);
    }

    [Then("the core update should be recorded without a file transport write")]
    public void ThenCoreUpdateWithoutTransportWrite()
    {
        var command = Assert.Single(Scenario.UpdatedGameServers);
        Assert.Equal(Scenario.GameServerId, command.GameServerId);
        Assert.Equal("SFTP CoD4 Server", command.Title);
        Assert.Equal("127.0.0.3", command.Hostname);
        Assert.Equal(28962, command.QueryPort);
        Assert.Equal(GameServerPlatform.Linux, command.Platform);
        Assert.False(command.AgentEnabled);
        Assert.True(command.FileTransportEnabled);
        Assert.Equal(FileTransportType.Sftp, command.FileTransportType);
        Assert.False(command.RconEnabled);
        Assert.False(command.BanFileSyncEnabled);
        Assert.Equal("/", command.BanFileRootPath);
        Assert.False(command.ServerListEnabled);
        Assert.Empty(Scenario.UpsertedConfigurations);
    }

    [Then("the core game server update should precede the failed SFTP write")]
    public void ThenCoreUpdatePrecedesFailedSftpWrite()
    {
        Assert.Equal(["UpdateGameServer", "UpsertConfiguration"], [.. Scenario.Operations]);
        Assert.Single(Scenario.UpdatedGameServers);
    }

    [Then("the file transport password should be visible")]
    public async Task ThenPasswordIsVisible()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("file-transport-password")).ToHaveAttributeAsync("type", "text");
    }

    [Then("the file transport password should be hidden")]
    public async Task ThenPasswordIsHidden()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("file-transport-password")).ToHaveAttributeAsync("type", "password");
    }

    [Then("successful file transport update feedback should be displayed")]
    public async Task ThenSuccessFeedback()
    {
        await Assertions.Expect(Browser.Page.GetByText("The game server SFTP CoD4 Server has been updated for CallOfDuty4")).ToBeVisibleAsync();
    }

    [Then("the file transport configuration failure warning should be displayed")]
    public async Task ThenFailureFeedback()
    {
        await Assertions.Expect(Browser.Page.GetByText("The game server SFTP CoD4 Server has been updated but some configuration sections failed to save: sftp")).ToBeVisibleAsync();
    }

    [Then("the file transport browser should report no errors")]
    public void ThenNoBrowserErrors()
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
    private string EditUrl => new Uri(Browser.Host.BaseAddress, $"/GameServers/Edit/{Scenario.GameServerId}").AbsoluteUri;
    private FileTransportScenario Scenario { get => field ?? throw new InvalidOperationException("Scenario not configured."); set; }

    private void AssertTransport(string ns, string host, int port, string user, string password, string mapsRoot, string? fingerprint)
    {
        var command = Assert.Single(Scenario.UpsertedConfigurations);
        Assert.Equal(ns, command.Namespace);
        using var json = JsonDocument.Parse(command.Configuration);
        var root = json.RootElement;
        Assert.Equal(host, root.GetProperty("hostname").GetString());
        Assert.Equal(port, root.GetProperty("port").GetInt32());
        Assert.Equal(user, root.GetProperty("username").GetString());
        Assert.Equal(password, root.GetProperty("password").GetString());
        Assert.Equal(mapsRoot, root.GetProperty("mapsRootPath").GetString());
        if (fingerprint is not null)
            Assert.Equal(fingerprint, root.GetProperty("hostKeyFingerprint").GetString());
    }

    private void Configure(string authenticationProfile, bool succeeds)
    {
        profile = authenticationProfile;
        Scenario = new FileTransportScenario(succeeds);
    }

    private async Task FillConnectionAsync(string host, string port, string user, string password, string fingerprint, string mapsRoot)
    {
        await Browser.Page.GetByTestId("file-transport-hostname").FillAsync(host);
        await Browser.Page.GetByTestId("file-transport-port").FillAsync(port);
        await Browser.Page.GetByTestId("file-transport-username").FillAsync(user);
        await Browser.Page.GetByTestId("file-transport-password").FillAsync(password);
        await Browser.Page.GetByTestId("sftp-host-key-fingerprint").FillAsync(fingerprint);
        await Browser.Page.GetByTestId("file-transport-maps-root").FillAsync(mapsRoot);
    }

    private async Task OpenFileTransportTabAsync()
    {
        await StartBrowserAsync();
        var response = await Browser.Page.GotoAsync(EditUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Browser.Page.Locator("#filetransfer-tab-btn").ClickAsync();
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(profile ?? throw new InvalidOperationException("Profile missing."), Scenario.ConfigureServices);
    }

    private async Task<IResponse> NativeSubmitAsync()
    {
        return await Browser.Page.RunAndWaitForResponseAsync(
            () => Browser.Page.Locator("#game-server-edit-form").EvaluateAsync("form => form.submit()"),
            IsEditResponse);
    }

    private async Task SubmitAndFollowAsync()
    {
        response = await Browser.Page.RunAndWaitForResponseAsync(
            () => Browser.Page.GetByTestId("game-server-save").ClickAsync(),
            IsEditResponse);
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/GameServers");
    }

    private bool IsEditResponse(IResponse candidate)
    {
        return candidate.Request.Method == "POST" && new Uri(candidate.Url).AbsolutePath == new Uri(EditUrl).AbsolutePath;
    }
}
