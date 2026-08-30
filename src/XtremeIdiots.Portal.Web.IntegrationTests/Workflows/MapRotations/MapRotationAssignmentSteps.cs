using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

[Binding]
public sealed class MapRotationAssignmentSteps
{
    private BrowserFixture? browser;
    private MapRotationAssignmentScenario? scenario;

    [Given("a map rotation assignment scenario for a map rotation deployer")]
    public void GivenAMapRotationAssignmentScenarioForAMapRotationDeployer()
    {
        scenario = new MapRotationAssignmentScenario();
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
        Assert.True(await Browser.Page.GetByTestId("assign-to-server-link").IsVisibleAsync());
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
        var options = await select.Locator("option").AllAsync();

        // Filter out the placeholder "-- Select Server --" option
        var serverOptions = new List<Microsoft.Playwright.ILocator>();
        foreach (var option in options)
        {
            var value = await option.GetAttributeAsync("value");
            if (!string.IsNullOrEmpty(value))
                serverOptions.Add(option);
        }

        Assert.Single(serverOptions);
        var optionValue = await serverOptions[0].GetAttributeAsync("value");
        Assert.Equal(MapRotationAssignmentScenario.PermittedServerId.ToString(), optionValue);

        var optionText = await serverOptions[0].InnerTextAsync();
        Assert.Contains("COD4x Permitted Server", optionText);
    }

    [Then("the non-permitted server should not appear in the server selector")]
    public async Task ThenTheNonPermittedServerShouldNotAppearInTheServerSelector()
    {
        var select = Browser.Page.GetByTestId("server-select");
        var html = await select.InnerHTMLAsync();
        Assert.DoesNotContain(MapRotationAssignmentScenario.NonPermittedServerId.ToString(), html);
        Assert.DoesNotContain("COD4x Non-Permitted Server", html);
    }

    [When("the deployer submits the assignment for the permitted server")]
    public async Task WhenTheDeployerSubmitsTheAssignmentForThePermittedServer()
    {
        var select = Browser.Page.GetByTestId("server-select");
        await select.SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());

        var responseTask = Browser.Page.WaitForResponseAsync(browserResponse =>
            browserResponse.Request.Method == "POST" &&
            new Uri(browserResponse.Url).AbsolutePath.StartsWith("/MapRotations/CreateAssignment", StringComparison.Ordinal));

        await Browser.Page.GetByTestId("assign-server-submit").ClickAsync();
        var response = await responseTask;
        Assert.Equal(302, response.Status);

        // Follow the redirect to the Details page
        await Browser.Page.WaitForURLAsync("**/MapRotations/Details/**");
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

    private MapRotationAssignmentScenario Scenario => scenario ?? throw new InvalidOperationException("The scenario has not been configured.");

    private string DetailsUrl => new Uri(Browser.Host.BaseAddress, $"/MapRotations/Details/{MapRotationAssignmentScenario.RotationId}").AbsoluteUri;

    private string CreateAssignmentUrl => new Uri(Browser.Host.BaseAddress, $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}").AbsoluteUri;

    private async Task StartBrowserAsync()
    {
        browser ??= await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.MapRotationDeployer,
            Scenario.ConfigureServices);
    }
}
