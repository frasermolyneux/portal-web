using Microsoft.Playwright;
using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

[Binding]
public sealed class MapRotationAssignmentSteps
{
    private BrowserFixture? browser;

    [Given("a map rotation assignment scenario for a map rotation deployer")]
    public void GivenAMapRotationAssignmentScenarioForAMapRotationDeployer()
    {
        Scenario = new MapRotationAssignmentScenario();
    }

    [When("the deployer views the rotation details")]
    public async Task WhenTheDeployerViewsTheRotationDetails()
    {
        await StartBrowserAsync();
        var response = await Browser.Page.GotoAsync(DetailsUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"Details page returned HTTP {response.Status}.");
    }

    [Then("the assign to server link should be visible")]
    public async Task ThenTheAssignToServerLinkShouldBeVisible()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("assign-to-server-link")).ToBeVisibleAsync();
    }

    [When("the deployer navigates to the create assignment page")]
    public async Task WhenTheDeployerNavigatesToTheCreateAssignmentPage()
    {
        await StartBrowserAsync();
        var response = await Browser.Page.GotoAsync(CreateAssignmentUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok, $"CreateAssignment page returned HTTP {response.Status}.");
    }

    [Then("only the permitted server should appear in the server selector")]
    public async Task ThenOnlyThePermittedServerShouldAppearInTheServerSelector()
    {
        var select = Browser.Page.GetByTestId("server-select");
        var serverOptions = select.Locator("option[value]:not([value=''])");
        await Assertions.Expect(serverOptions).ToHaveCountAsync(1);
        await Assertions.Expect(serverOptions).ToHaveAttributeAsync("value", MapRotationAssignmentScenario.PermittedServerId.ToString());
        await Assertions.Expect(serverOptions).ToContainTextAsync("COD4x Permitted Server");
    }

    [Then("the non-permitted server should not appear in the server selector")]
    public async Task ThenTheNonPermittedServerShouldNotAppearInTheServerSelector()
    {
        var select = Browser.Page.GetByTestId("server-select");
        await Assertions.Expect(select.Locator($"option[value='{MapRotationAssignmentScenario.NonPermittedServerId}']")).ToHaveCountAsync(0);
        await Assertions.Expect(select).Not.ToContainTextAsync(MapRotationAssignmentScenario.NonPermittedServerId.ToString());
        await Assertions.Expect(select).Not.ToContainTextAsync("COD4x Non-Permitted Server");
    }

    [When("the deployer submits the assignment for the permitted server")]
    public async Task WhenTheDeployerSubmitsTheAssignmentForThePermittedServer()
    {
        var select = Browser.Page.GetByTestId("server-select");
        await select.SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());

        var response = await Browser.Page.RunAndWaitForResponseAsync(
            () => Browser.Page.GetByTestId("assign-server-submit").ClickAsync(),
            browserResponse => browserResponse.Request.Method == "POST" &&
                new Uri(browserResponse.Url).AbsolutePath == "/MapRotations/CreateAssignment");
        Assert.Equal(302, response.Status);

        // Follow the redirect to the Details page
        await Browser.Page.WaitForURLAsync(DetailsUrl);
        await Assertions.Expect(Browser.Page.GetByTestId("assign-to-server-link")).ToBeVisibleAsync();
    }

    [Then("the assignment should be created with the correct server")]
    public void ThenTheAssignmentShouldBeCreatedWithTheCorrectServer()
    {
        var created = Assert.Single(Scenario.CreatedAssignments);
        Assert.Equal(MapRotationAssignmentScenario.RotationId, created.MapRotationId);
        Assert.Equal(MapRotationAssignmentScenario.PermittedServerId, created.GameServerId);
    }

    [Then("the map rotation assignment browser should report no errors")]
    public void ThenTheMapRotationAssignmentBrowserShouldReportNoErrors()
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

    private MapRotationAssignmentScenario Scenario { get => field ?? throw new InvalidOperationException("The scenario has not been configured."); set; }

    private string DetailsUrl => new Uri(Browser.Host.BaseAddress, $"/MapRotations/Details/{MapRotationAssignmentScenario.RotationId}").AbsoluteUri;

    private string CreateAssignmentUrl => new Uri(Browser.Host.BaseAddress, $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}").AbsoluteUri;

    private async Task StartBrowserAsync()
    {
        browser ??= await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.MapRotationDeployer,
            Scenario.ConfigureServices);
    }
}
