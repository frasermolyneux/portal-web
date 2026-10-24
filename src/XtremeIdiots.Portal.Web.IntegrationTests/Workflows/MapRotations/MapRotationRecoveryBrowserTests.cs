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

        foreach (var action in new[] { "Apply rotation", "Verify files" })
            await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = action, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#assignment-tools")).Not.ToHaveAttributeAsync("open", "");
        await browser.Page.Locator("#assignment-tools > summary").ClickAsync();
        foreach (var action in new[] { "Repair files", "Reapply configuration", "Deactivate" })
            await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = action, Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator(".detail-value .badge.bg-warning").Filter(new() { HasText = "Update needed" }))
            .ToHaveCountAsync(state == DeploymentState.Synced ? 2 : 1);
        await Assertions.Expect(browser.Page.GetByText("Rotation version", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByText("Deployed version", new() { Exact = true })).ToHaveCountAsync(0);
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
    public async Task RotationDetails_LinksToSingleServerActionPageWithoutLoadingActivity()
    {
        var scenario = new MapRotationRecoveryScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/Details/{scenario.RotationId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = true }))
            .ToHaveAttributeAsync("href", $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}");
        foreach (var action in new[] { "Apply rotation", "Verify files", "Repair files", "Reapply configuration" })
            await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = action, Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.Locator("th")).Not.ToContainTextAsync(["Versions"]);
        scenario.Repository.Verify(x => x.MapRotations.V1.GetAssignmentOperations(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(AssignmentOperationStatus.Cancelled)]
    [InlineData(AssignmentOperationStatus.Failed)]
    public async Task TerminalOperation_WithUncertainBusyState_RequiresServiceInvestigationWithoutPortalReset(AssignmentOperationStatus operationStatus)
    {
        var scenario = new MapRotationRecoveryScenario(DeploymentState.Syncing, busy: true,
            operationStatus: operationStatus);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("body")).ToContainTextAsync("Ask a portal administrator to investigate before retrying");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Retry operation finalization", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Terminate", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToHaveCountAsync(0);
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

    [Theory]
    [InlineData(true, "bg-success", "Completed successfully.")]
    [InlineData(null, "bg-secondary", "Finished. Refresh to check the recorded result.")]
    public async Task DurableCompletion_OnlyConfirmedSuccessIsGreen(bool? success, string colour, string message)
    {
        var scenario = new MapRotationRecoveryScenario(busy: true);
        scenario.Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{scenario.AssignmentId}", "Completed",
                    DateTime.UtcNow, DateTime.UtcNow, null, Success: success)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator(".progress-label")).ToHaveTextAsync(message);
        await Assertions.Expect(browser.Page.Locator(".progress-bar." + colour)).ToHaveCountAsync(1);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Verification_OnlyFailedMapMessagesAreRed()
    {
        var scenario = new MapRotationRecoveryScenario(busy: true);
        scenario.Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{scenario.AssignmentId}", "Running",
                    DateTime.UtcNow, DateTime.UtcNow,
                    new OrchestrationProgressDto("Verify", 3, 3,
                    [
                        new MapProgressDto("mp_ready", "Skipped", "Already verified"),
                        new MapProgressDto("mp_complete", "Completed", "All required files are present"),
                        new MapProgressDto("mp_missing", "Failed", "Required file is missing")
                    ]))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator(".progress-map-list small.text-danger")).ToHaveTextAsync("Required file is missing");
        await Assertions.Expect(browser.Page.Locator(".progress-map-list small.text-muted")).ToHaveTextAsync(
            ["Already verified", "All required files are present"]);
        await Assertions.Expect(browser.Page.Locator(".progress-map-list .badge").Filter(new() { HasText = "Ready" })).ToHaveCountAsync(1);
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
            Body = /*lang=json,strict*/ """{"draw":1,"recordsTotal":1,"recordsFiltered":1,"data":[{"mapRotationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","gameType":"CallOfDuty4","title":"Synthetic assigned rotation","gameMode":"dm","status":"Published","mapCount":1,"serverCount":1,"updatedAt":"2026-10-24","createdByDisplayName":"<img src=x onerror='window.rotationAuthorExecuted=true'>"}]}"""
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator(".ibox-content.datatable-tight")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToContainTextAsync("Assigned to 1 server");
        await Assertions.Expect(browser.Page.Locator("#dataTable")).Not.ToContainTextAsync("Active on");
        await Assertions.Expect(browser.Page.Locator("#dataTable tbody a.btn")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Open", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Author", Exact = true })).ToHaveCountAsync(0);
        var authorHtml = await browser.Page.EvaluateAsync<string>("() => $('#dataTable').DataTable().cell(0, 'createdBy:name').node().innerHTML");
        Assert.Contains("&lt;img", authorHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", authorHtml, StringComparison.Ordinal);
        var request = await browser.Page.RunAndWaitForRequestAsync(
            () => browser.Page.Locator("#filterMyRotations").ClickAsync(),
            request => request.Method == "POST" && new Uri(request.Url).AbsolutePath == "/MapRotations/GetMapRotationsAjax");
        using var payload = System.Text.Json.JsonDocument.Parse(request.PostData!);
        var authorFilter = payload.RootElement.GetProperty("columns").EnumerateArray()
            .Single(column => column.GetProperty("name").GetString() == "createdBy");
        Assert.Equal("11111111-1111-1111-1111-111111111111", authorFilter.GetProperty("search").GetProperty("value").GetString());
        browser.AssertNoBrowserErrors();
    }
}
