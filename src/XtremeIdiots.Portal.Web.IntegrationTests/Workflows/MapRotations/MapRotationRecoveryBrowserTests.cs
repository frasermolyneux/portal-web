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
    [Fact]
    public async Task CombinedAssignment_OneServerRowAndHub_ChildNavigationAndPrimaryAction()
    {
        var scenario = new MapRotationRecoveryScenario(combined: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/Details/{scenario.RotationId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("body")).ToContainTextAsync("Main rotation + AACP map picker");

        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.ChildAssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page).ToHaveURLAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator(".ibox-content > dl .detail-value").Filter(new() { HasText = "Map picker:" })).ToHaveCountAsync(2);
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/ApplyAssignment");
        Assert.Equal(302, response.Status);
        scenario.Sync.Verify(x => x.TriggerApply(scenario.AssignmentId, It.IsAny<CancellationToken>()), Times.Once);
        scenario.Sync.Verify(x => x.TriggerApply(scenario.ChildAssignmentId, It.IsAny<CancellationToken>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task CombinedAssignment_ChildFailureIsNotHiddenByPrimarySuccess()
    {
        var scenario = new MapRotationRecoveryScenario(combined: true, childFailed: true, busy: true,
            operationStatus: AssignmentOperationStatus.Succeeded, completedAt: DateTime.UtcNow);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator(".alert-danger")).ToContainTextAsync("Synthetic AACP configuration failure");
        await Assertions.Expect(browser.Page.Locator("#operation-history > summary .badge.bg-danger")).ToHaveTextAsync("Needs attention");
        await browser.Page.Locator("#operation-history > summary").ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#operationsTable")).ToContainTextAsync("Synthetic child operation failed");
        await Assertions.Expect(browser.Page.Locator("#operationsTable tbody > tr")).ToHaveCountAsync(2);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task CombinedAssignment_ChildRunningHistory_CancelsVerifiedSharedInstanceWithoutFinalizing()
    {
        var scenario = new MapRotationRecoveryScenario(combined: true, childBusy: true);
        var instanceId = $"maprot-apply-{scenario.AssignmentId}";
        scenario.Sync.Setup(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(false, CancellationRequested: true));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply rotation", Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.Locator("#operation-history[open]")).ToHaveCountAsync(1);
        var cancel = browser.Page.Locator("form[action='/MapRotations/CancelOperation']");
        await Assertions.Expect(cancel.Locator("input[name='assignmentId']")).ToHaveValueAsync(scenario.ChildAssignmentId.ToString());
        browser.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => cancel.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/CancelOperation");
        Assert.Equal(302, response.Status);
        await Assertions.Expect(browser.Page.Locator("#operationsContainer")).ToHaveAttributeAsync("data-pending-instance-id", instanceId);
        scenario.Sync.Verify(x => x.TerminateOrchestration(instanceId, It.IsAny<CancellationToken>()), Times.Once);
        scenario.Repository.Verify(x => x.MapRotations.V1.UpdateAssignmentOperation(
            It.IsAny<Guid>(), It.IsAny<AssignmentOperationStatus>(), It.IsAny<string>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task AssignmentForms_AacpToggleRequiresPathAndPostsOneAtomicCreate()
    {
        var scenario = new MapRotationAssignmentScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}").AbsoluteUri);
        var checkbox = browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true });
        var path = browser.Page.GetByLabel("AACP config file", new() { Exact = true });
        await Assertions.Expect(path).Not.ToBeVisibleAsync();
        await checkbox.CheckAsync();
        await Assertions.Expect(path).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#AacpConfigFilePath[required]")).ToHaveCountAsync(1);
        Assert.False(await path.EvaluateAsync<bool>("input => input.checkValidity()"));
        await path.FillAsync("aacp.cfg");
        await checkbox.UncheckAsync();
        await Assertions.Expect(browser.Page.Locator("#AacpConfigFilePath[required]")).ToHaveCountAsync(0);
        await checkbox.CheckAsync();
        await Assertions.Expect(path).ToHaveValueAsync("aacp.cfg");
        await browser.Page.GetByTestId("server-select").SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        await browser.Page.GetByLabel("Config File", new() { Exact = true }).FillAsync("server.cfg");
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByTestId("assign-server-submit").ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/CreateAssignment");
        Assert.Equal(302, response.Status);
        var created = Assert.Single(scenario.CreatedAssignments);
        Assert.Equal("aacp.cfg", created.AacpConfigFilePath);
        Assert.Equal("sv_maprotation", created.ConfigVariableName);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task CombinedEdit_FromChild_LoadsCheckedAacpAndRejectsActiveUncheck()
    {
        var scenario = new MapRotationRecoveryScenario(combined: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/EditAssignment/{scenario.ChildAssignmentId}").AbsoluteUri);
        await Assertions.Expect(browser.Page).ToHaveURLAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/EditAssignment/{scenario.AssignmentId}").AbsoluteUri);
        var checkbox = browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true });
        await Assertions.Expect(checkbox).ToBeCheckedAsync();
        await Assertions.Expect(browser.Page.GetByLabel("AACP config file", new() { Exact = true })).ToHaveValueAsync("aacp.cfg");
        await checkbox.UncheckAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save Changes", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("[data-valmsg-for='AacpEnabled']")).ToContainTextAsync("Deactivate first");
        scenario.Repository.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations.UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

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

    [Theory]
    [InlineData(AssignmentOperationStatus.Succeeded, true, "Completed", "bg-success")]
    [InlineData(AssignmentOperationStatus.Failed, false, "Failed", "bg-danger")]
    [InlineData(AssignmentOperationStatus.Cancelled, true, "Cancelled", "bg-secondary")]
    public async Task CompletedActivity_SummarizesActionResultAndTimeBeforeExpanding(
        AssignmentOperationStatus status, bool hasCompletionTime, string result, string colour)
    {
        DateTime? completedAt = hasCompletionTime ? new DateTime(2026, 10, 24, 19, 45, 0, DateTimeKind.Utc) : null;
        var scenario = new MapRotationRecoveryScenario(busy: true, operationStatus: status, completedAt: completedAt);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);
        var summary = browser.Page.Locator("#operation-history > summary");
        await Assertions.Expect(summary).ToContainTextAsync("View recent actions and results");
        await Assertions.Expect(summary).ToContainTextAsync("Last action: Apply rotation: files");
        await Assertions.Expect(summary.Locator(".badge." + colour)).ToHaveTextAsync(result);
        await Assertions.Expect(summary).ToContainTextAsync(hasCompletionTime ? "Finished" : "Started");
        await Assertions.Expect(summary.Locator("time")).ToBeVisibleAsync();
        if (completedAt.HasValue)
            await Assertions.Expect(summary.Locator("time")).ToHaveAttributeAsync("datetime", completedAt.Value.ToString("o"));
        await Assertions.Expect(browser.Page.Locator("#operationsTable")).Not.ToBeVisibleAsync();
        await summary.ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#operationsTable")).ToBeVisibleAsync();
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
            Body = /*lang=json,strict*/ """
                {"draw":1,"recordsTotal":2,"recordsFiltered":2,"data":[
                  {"mapRotationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","gameType":"CallOfDuty4","title":"Z assigned rotation","gameMode":"dm","status":"Published","mapCount":1,"serverCount":1,"updatedAt":"2026-10-24","createdByDisplayName":"<img src=x onerror='window.rotationAuthorExecuted=true'>"},
                  {"mapRotationId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","gameType":"CallOfDuty4","title":"A unassigned rotation","gameMode":"dm","status":"Published","mapCount":1,"serverCount":0,"updatedAt":"2026-10-24"}
                ]}
                """
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator(".ibox-content.datatable-tight")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToContainTextAsync("Assigned to 1 server");
        await Assertions.Expect(browser.Page.Locator("#dataTable")).Not.ToContainTextAsync("Active on");
        await Assertions.Expect(browser.Page.Locator("#dataTable .badge.bg-info").Filter(new() { HasText = "Assigned to 1 server" })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#dataTable .badge.bg-secondary").Filter(new() { HasText = "Unassigned" })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#dataTable tbody a.btn")).ToHaveCountAsync(2);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Open", Exact = true }).First).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Author", Exact = true })).ToHaveCountAsync(0);
        var order = await browser.Page.EvaluateAsync<string>("() => JSON.stringify($('#dataTable').DataTable().order())");
        Assert.Equal("[[5,\"desc\"]]", order);
        await browser.Page.EvaluateAsync("""
            () => {
                const state = $('#dataTable').DataTable().state();
                state._mapRotationsStructureVersion = 4;
                state.order = [[1, 'asc']];
                localStorage.setItem('DataTables_dataTable_' + location.pathname, JSON.stringify(state));
            }
            """);
        var reloadRequest = await browser.Page.RunAndWaitForRequestAsync(
            () => browser.Page.ReloadAsync(),
            request => request.Method == "POST" && new Uri(request.Url).AbsolutePath == "/MapRotations/GetMapRotationsAjax");
        using var reloadPayload = System.Text.Json.JsonDocument.Parse(reloadRequest.PostData!);
        var defaultOrder = reloadPayload.RootElement.GetProperty("order")[0];
        Assert.Equal(5, defaultOrder.GetProperty("column").GetInt32());
        Assert.Equal("desc", defaultOrder.GetProperty("dir").GetString());
        await Assertions.Expect(browser.Page.Locator("#dataTable")).ToContainTextAsync("Assigned to 1 server");
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
