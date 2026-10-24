using Microsoft.Playwright;
using Moq;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

[Trait("Category", "Browser")]
public sealed class MapRotationRecoveryBrowserTests
{
    [Theory]
    [InlineData(DeploymentState.Pending)]
    [InlineData(DeploymentState.Synced)]
    [InlineData(DeploymentState.PartiallyDeployed)]
    [InlineData(DeploymentState.Failed)]
    public async Task ActiveAndStaleAssignment_ExposesIndependentRecoveryAndAllErrors(DeploymentState state)
    {
        var scenario = new MapRotationRecoveryScenario(state);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);

        foreach (var action in new[] { "Apply rotation", "Repair files", "Verify files", "Reapply configuration" })
            await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = action, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Unassign", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.Locator(".alert-danger")).ToContainTextAsync("Synthetic verification error on active configuration");
        await Assertions.Expect(browser.Page.Locator("body")).Not.ToContainTextAsync("server still needs a restart");

        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/ApplyAssignment");
        Assert.Equal(302, response.Status);
        await Assertions.Expect(browser.Page.Locator("#operationsContainer")).ToHaveAttributeAsync("data-pending-instance-id", $"maprot-apply-{scenario.AssignmentId}");
        await Assertions.Expect(browser.Page.Locator("body")).ToContainTextAsync("Apply rotation queued");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task BusyAssignment_DoesNotExposeNewRecoveryActions()
    {
        var scenario = new MapRotationRecoveryScenario(busy: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Repair files", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToBeVisibleAsync();
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task DurableCompletion_BusinessFailureIsShownAsFailure()
    {
        var scenario = new MapRotationRecoveryScenario(busy: true);
        scenario.Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{scenario.AssignmentId}", "Completed",
                    DateTime.UtcNow, DateTime.UtcNow, null, Success: false, Error: "Synthetic linked target failure")));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator(".progress-label")).ToContainTextAsync("Operation failed");
        await Assertions.Expect(browser.Page.Locator("#operationsTable")).ToContainTextAsync("Synthetic linked target failure");
        await Assertions.Expect(browser.Page.Locator(".progress-bar.bg-danger")).ToHaveCountAsync(1);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Index_InitializesTightTableWithoutJQueryClassListError()
    {
        var scenario = new MapRotationRecoveryScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.RouteAsync("**/MapRotations/GetMapRotationsAjax**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "application/json",
            Body = /*lang=json,strict*/ """{"draw":1,"recordsTotal":0,"recordsFiltered":0,"data":[]}"""
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator(".ibox-content.datatable-tight")).ToHaveCountAsync(1);
        browser.AssertNoBrowserErrors();
    }
}
