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

    [Theory]
    [InlineData(AssignmentOperationStatus.Cancelled)]
    [InlineData(AssignmentOperationStatus.Failed)]
    public async Task TerminalOperation_WithPendingStateReset_OffersPortalFinalizationRetry(AssignmentOperationStatus operationStatus)
    {
        var scenario = new MapRotationRecoveryScenario(DeploymentState.Syncing, busy: true,
            operationStatus: operationStatus);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Retry operation finalization", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true })).ToHaveCountAsync(0);
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
    public async Task PendingApply_ErrorIsVisibleBeforeRepositoryOperationRowExists()
    {
        var scenario = new MapRotationRecoveryScenario();
        scenario.Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{scenario.AssignmentId}", "Completed",
                    DateTime.UtcNow, DateTime.UtcNow,
                    new OrchestrationProgressDto("Apply", 1, 1, [new MapProgressDto("configuration", "Failed", "Synthetic bootstrap failure")]),
                    Success: false)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".pending-bootstrap-row .operation-error")).ToHaveTextAsync("Synthetic bootstrap failure");
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
            Body = /*lang=json,strict*/ """{"draw":1,"recordsTotal":1,"recordsFiltered":1,"data":[{"mapRotationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","gameType":"CallOfDuty4","title":"Synthetic assigned rotation","gameMode":"dm","status":"Published","mapCount":1,"serverCount":1,"updatedAt":"2026-10-24"}]}"""
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator(".ibox-content.datatable-tight")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToContainTextAsync("Assigned to 1 server");
        await Assertions.Expect(browser.Page.Locator("#dataTable")).Not.ToContainTextAsync("Active on");
        browser.AssertNoBrowserErrors();
    }
}
