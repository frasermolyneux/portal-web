using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using MX.Api.Client;
using System.Net;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Config;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Files;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

[Trait("Category", "Browser")]
public sealed class MapRotationRecoveryBrowserTests
{
    [Theory]
    [InlineData(false, 1280)]
    [InlineData(true, 1280)]
    [InlineData(false, 390)]
    [InlineData(true, 390)]
    public async Task ConfigValidation_SeparatesInlineErrorsAndPersistentAlerts(bool fromApply, int width)
    {
        var scenario = new MapRotationRecoveryScenario(mainOnly: true);
        scenario.Servers.Setup(x => x.Config.V1.GetConfigFile(It.IsAny<Guid>(), "synthetic.cfg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigFileContentDto>(HttpStatusCode.OK, new ApiResponse<ConfigFileContentDto>(
                new ConfigFileContentDto("synthetic.cfg", "//set sv_maprotation \"map mp_bog\""))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.SetViewportSizeAsync(width, 1000);
        var page = fromApply ? "AssignmentStatus" : "EditAssignment";
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/{page}/{scenario.AssignmentId}").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = fromApply ? "Apply rotation" : "Save Changes", Exact = true }).ClickAsync();

        await Assertions.Expect(browser.Page.GetByLabel("Config File", new() { Exact = true })).ToHaveValueAsync("synthetic.cfg");
        var error = fromApply
            ? browser.Page.Locator(".validation-summary-errors.alert[role='alert']")
            : browser.Page.Locator("#ConfigFilePathError");
        await Assertions.Expect(error).ToBeVisibleAsync();
        await Assertions.Expect(error).ToContainTextAsync("Select a different file");
        await Assertions.Expect(error).ToContainTextAsync("sv_maprotation");
        if (fromApply)
        {
            await Assertions.Expect(error.Locator("strong")).ToHaveTextAsync("Check assignment settings");
            await Assertions.Expect(error.Locator("ul")).ToHaveCountAsync(0);
        }
        else
        {
            Assert.Equal("block", await error.EvaluateAsync<string>("element => getComputedStyle(element).display"));
            var errorBounds = await error.BoundingBoxAsync();
            var helpBounds = await browser.Page.Locator("#ConfigFilePathHelp").BoundingBoxAsync();
            Assert.NotNull(errorBounds);
            Assert.NotNull(helpBounds);
            Assert.True(helpBounds.Y >= errorBounds.Y + errorBounds.Height);
        }

        Assert.True(await error.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"));
        scenario.Repository.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Never);
        scenario.Sync.Verify(x => x.TriggerApply(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Status_PutsRecordedErrorsInExpandableWrappingTechnicalDetails()
    {
        const string diagnostic = "Task 'WriteAndVerifyConfigVariable' failed: synthetic-diagnostic-" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
            " <script>window.rotationDiagnosticExecuted=true</script>";
        var scenario = new MapRotationRecoveryScenario(mainOnly: true, busy: true,
            operationStatus: AssignmentOperationStatus.Failed, completedAt: DateTime.UtcNow.AddMinutes(-1),
            error: diagnostic, activationState: ActivationState.Failed);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.SetViewportSizeAsync(390, 1000);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}").AbsoluteUri);

        var alert = browser.Page.Locator(".alert-danger[role='alert']");
        await Assertions.Expect(alert).ToContainTextAsync("Check the selected file and variable");
        await Assertions.Expect(alert).Not.ToContainTextAsync("synthetic-diagnostic");
        await Assertions.Expect(alert.GetByRole(AriaRole.Link, new() { Name = "Edit assignment settings" })).ToBeVisibleAsync();
        var diagnosticText = browser.Page.Locator("#assignment-details pre");
        await Assertions.Expect(diagnosticText).Not.ToBeVisibleAsync();
        await browser.Page.Locator("#assignment-details > summary").ClickAsync();
        await Assertions.Expect(diagnosticText).ToHaveTextAsync(diagnostic);
        Assert.True(await diagnosticText.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"));
        await browser.Page.Locator("#operation-history > summary").ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".operation-error")).Not.ToContainTextAsync("synthetic-diagnostic");
        await browser.Page.Locator("#operationsTable tbody details > summary").ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".operation-diagnostics")).ToHaveTextAsync(diagnostic);
        Assert.False(await browser.Page.EvaluateAsync<bool>("window.rotationDiagnosticExecuted === true"));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Clone_Cod4xKeepsItsGameTypeInTheCreateFormAndCatalogueFilter()
    {
        var scenario = new MapRotationRecoveryScenario(gameType: GameType.CallOfDuty4x);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);

        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/Clone/{scenario.RotationId}").AbsoluteUri);

        await Assertions.Expect(browser.Page.Locator("#GameType")).ToHaveValueAsync("CallOfDuty4x");
        await Assertions.Expect(browser.Page.Locator("#Title")).ToHaveValueAsync("Synthetic Recovery (Copy)");
        await browser.Page.RouteAsync("**/MapRotations/GetMapRotationsAjax/**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = /*lang=json,strict*/ """{"draw":1,"recordsTotal":0,"recordsFiltered":0,"data":[]}"""
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations?gameType=CallOfDuty4x").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#filterGameType")).ToHaveValueAsync("CallOfDuty4x");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task EditAssignment_InvalidAacpTargetRemainsVisibleWhenPickerUnchecked()
    {
        var scenario = new MapRotationRecoveryScenario(mainOnly: true);
        scenario.Repository.Setup(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiHttpRequestException("synthetic-private-diagnostic", HttpStatusCode.BadRequest,
                new ApiResponse(new ApiError("INVALID_AACP_TARGET", "synthetic-private-diagnostic"))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        var path = $"/MapRotations/EditAssignment/{scenario.AssignmentId}";
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, path).AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true })).Not.ToBeCheckedAsync();
        await Assertions.Expect(browser.Page.Locator("#aacp-config")).Not.ToBeVisibleAsync();

        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save Changes", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == path);

        Assert.Equal(200, response.Status);
        var summary = browser.Page.Locator(".validation-summary-errors");
        await Assertions.Expect(summary).ToBeVisibleAsync();
        await Assertions.Expect(summary).ToContainTextAsync("Choose valid main and AACP configuration files");
        await Assertions.Expect(browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true })).Not.ToBeCheckedAsync();
        await Assertions.Expect(browser.Page.GetByLabel("Config File", new() { Exact = true })).ToHaveValueAsync("synthetic.cfg");
        await Assertions.Expect(browser.Page.Locator("body")).Not.ToContainTextAsync("synthetic-private-diagnostic");
        scenario.Repository.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Once);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task AssignmentRejection_ShowsErrorBesideServerAndPreservesForm()
    {
        var scenario = new MapRotationAssignmentScenario();
        scenario.RepositoryClient.Setup(x => x.MapRotations.V1.CreateServerAssignment(
            It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiHttpRequestException("synthetic-private-diagnostic", HttpStatusCode.BadRequest,
                new ApiResponse(new ApiError("GAME_TYPE_MISMATCH", "synthetic-private-diagnostic", "UntrustedTarget"))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress,
            $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}").AbsoluteUri);
        await browser.Page.GetByTestId("server-select").SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        await browser.Page.GetByLabel("Config File", new() { Exact = true }).FillAsync("server.cfg");

        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByTestId("assign-server-submit").ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/CreateAssignment");

        Assert.Equal(200, response.Status);
        var error = browser.Page.Locator("[data-valmsg-for='GameServerId']");
        await Assertions.Expect(error).ToBeVisibleAsync();
        await Assertions.Expect(error).ToContainTextAsync("The selected server does not support this rotation");
        await Assertions.Expect(browser.Page.GetByTestId("server-select")).ToHaveValueAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        await Assertions.Expect(browser.Page.GetByLabel("Config File", new() { Exact = true })).ToHaveValueAsync("server.cfg");
        await Assertions.Expect(browser.Page.Locator("body")).Not.ToContainTextAsync("synthetic-private-diagnostic");
        Assert.Empty(scenario.CreatedAssignments);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Import_ServerPickerUsesScopedBrowseAndExistingPreviewWithoutExposingOtherConfig()
    {
        var scenario = new MapRotationImportScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        var endpoint = $"/api/file-browse/{MapRotationAssignmentScenario.PermittedServerId}/browse";
        await browser.Page.RouteAsync("**" + endpoint + "?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = /*lang=json,strict*/ """
                {"currentPath":"/","parentPath":null,"items":[
                    {"name":"maprotation.cfg","fullPath":"/configs/maprotation.cfg","type":"File","size":100},
                    {"name":"server.log","fullPath":"/server.log","type":"File","size":100}
                ]}
                """
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapRotations/Import").AbsoluteUri);
        await browser.Page.Locator("#GameType").SelectOptionAsync("CallOfDuty4x");
        var source = browser.Page.GetByLabel("Read config from a game server", new() { Exact = true });
        var local = browser.Page.GetByLabel("Config Content", new() { Exact = true });
        await Assertions.Expect(local).ToBeVisibleAsync();
        await source.CheckAsync();
        await Assertions.Expect(local).Not.ToBeVisibleAsync();
        var server = browser.Page.GetByLabel("Game Server", new() { Exact = true });
        await Assertions.Expect(server.Locator($"option[value='{MapRotationAssignmentScenario.NonPermittedServerId}']")).ToHaveCountAsync(0);
        await server.SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        await browser.Page.Locator("#GameType").SelectOptionAsync("CallOfDuty4");
        await Assertions.Expect(server).ToHaveValueAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        await browser.Page.Locator("#GameType").SelectOptionAsync("CallOfDuty4x");
        await Assertions.Expect(server).ToHaveValueAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        var browse = browser.Page.GetByRole(AriaRole.Button, new() { Name = "Browse", Exact = true });
        var response = await browser.Page.RunAndWaitForResponseAsync(() => browse.ClickAsync(),
            response => response.Request.Method == "GET" && new Uri(response.Url).AbsolutePath == endpoint);
        Assert.Contains("purpose=map-rotation-import", new Uri(response.Url).Query, StringComparison.Ordinal);
        await Assertions.Expect(browser.Page.Locator("#fileBrowseListingBody tr").Filter(new() { HasText = "server.log" })).ToHaveClassAsync("opacity-50");
        await browser.Page.Locator("#fileBrowseListingBody tr").Filter(new() { HasText = "maprotation.cfg" }).ClickAsync();
        await browser.Page.Locator("#fileBrowseSelectBtn").ClickAsync();
        var path = browser.Page.GetByLabel("Server Config File", new() { Exact = true });
        await Assertions.Expect(path).ToHaveValueAsync(MapRotationImportScenario.ConfigPath);
        await source.UncheckAsync();
        await Assertions.Expect(local).ToBeVisibleAsync();
        await source.CheckAsync();
        await Assertions.Expect(path).ToHaveValueAsync(MapRotationImportScenario.ConfigPath);

        var preview = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Parse & Preview", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/MapRotations/Import");
        Assert.Equal(200, preview.Status);
        await Assertions.Expect(browser.Page.Locator("input[name$='.Title']")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("body")).ToContainTextAsync("mp_alpha");
        await Assertions.Expect(browser.Page.Locator("body")).ToContainTextAsync("mp_beta");
        await Assertions.Expect(browser.Page.Locator("body")).Not.ToContainTextAsync("synthetic-secret");
        var inputValues = await browser.Page.Locator("input, textarea").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.value)");
        Assert.All(inputValues, value =>
        {
            Assert.DoesNotContain("synthetic-secret", value, StringComparison.Ordinal);
            Assert.DoesNotContain("syntheticAuthor", value, StringComparison.Ordinal);
        });
        scenario.Servers.Verify(x => x.Files.V1.GetContent(MapRotationAssignmentScenario.PermittedServerId,
            It.Is<GetFileContentQueryDto>(q => q.Path == MapRotationImportScenario.ConfigPath && q.RangeEnd == 1_000_000),
            It.IsAny<CancellationToken>()), Times.Once);
        scenario.Repository.Verify(x => x.Maps.V1.GetMaps(GameType.CallOfDuty4x, It.IsAny<string[]?>(), null, null,
            0, 100, MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(Mock.Get(scenario.Repository.Object.MapRotations.V1).Invocations,
            invocation => invocation.Method.Name.StartsWith("Create", StringComparison.Ordinal));
        Assert.DoesNotContain(Mock.Get(scenario.Repository.Object.Maps.V1).Invocations,
            invocation => invocation.Method.Name.StartsWith("Create", StringComparison.Ordinal));
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapEditor_SuggestionsShowAtLeastSixCompleteMapsAndCanSelectBeyondTheFold(bool edit)
    {
        var scenario = new MapRotationRecoveryScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        var maps = Enumerable.Range(0, 12).Select(index => new
        {
            id = Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}").ToString(),
            text = $"mp_test_{index}",
            imageUrl = "/images/noimage.jpg"
        }).ToArray();
        await browser.Page.RouteAsync("**/MapSearch/Maps?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = System.Text.Json.JsonSerializer.Serialize(maps)
        }));
        var url = edit ? $"/MapRotations/Edit/{scenario.RotationId}" : "/MapRotations/Create";
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, url).AbsoluteUri);
        await browser.Page.Locator("#mapSearch").FillAsync("mp_test");
        var suggestions = browser.Page.Locator(".map-suggestions");
        await Assertions.Expect(suggestions.Locator("button")).ToHaveCountAsync(12);
        await suggestions.ScrollIntoViewIfNeededAsync();
        var visibleMaps = await suggestions.EvaluateAsync<int>("""
            list => {
                const bounds = list.getBoundingClientRect();
                const card = list.closest('.ibox').getBoundingClientRect();
                return [...list.children].filter(item => {
                    const row = item.getBoundingClientRect();
                    return row.top >= Math.max(0, bounds.top) && row.bottom <= Math.min(innerHeight, bounds.bottom, card.bottom);
                }).length;
            }
            """);
        Assert.True(visibleMaps >= 6, $"Only {visibleMaps} complete map suggestions were visible.");
        await suggestions.GetByRole(AriaRole.Option, new() { Name = "mp_test_11", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#selectedMapsList")).ToContainTextAsync("mp_test_11");
        await Assertions.Expect(browser.Page.Locator("#mapIdsContainer input").Last).ToHaveValueAsync(maps[11].id);
        await browser.Page.Locator("#mapSearch").FillAsync("mp_test");
        await Assertions.Expect(suggestions).ToBeVisibleAsync();
        await browser.Page.Locator("#mapSearch").PressAsync("ArrowDown");
        await browser.Page.Locator("#mapSearch").PressAsync("Enter");
        await Assertions.Expect(browser.Page.Locator("#selectedMapsList")).ToContainTextAsync("mp_test_0");
        browser.AssertNoBrowserErrors();
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentForms_AacpBrowseSelectsOnlyItsConfigAndExplainsAutomaticVariable(bool edit)
    {
        var create = new MapRotationAssignmentScenario(fileTransportEnabled: true);
        var recovery = new MapRotationRecoveryScenario(combined: true, fileTransportEnabled: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer,
            edit ? recovery.ConfigureServices : create.ConfigureServices);
        var endpoint = $"/api/file-browse/{MapRotationAssignmentScenario.PermittedServerId}/browse";
        await browser.Page.RouteAsync("**" + endpoint + "?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = /*lang=json,strict*/ """
                {"currentPath":"/","parentPath":null,"items":[
                    {"name":"advancedacp.cfg","fullPath":"/configs/advancedacp.cfg","type":"File","size":100},
                    {"name":"mapcontrol.cfg","fullPath":"/configs/mapcontrol.cfg","type":"File","size":100},
                    {"name":"server.log","fullPath":"/logs/server.log","type":"File","size":200}
                ]}
                """
        }));
        var url = edit ? $"/MapRotations/EditAssignment/{recovery.AssignmentId}"
            : $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}";
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, url).AbsoluteUri);
        await browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true }).CheckAsync();
        var browse = browser.Page.Locator("#AacpConfigFilePathBrowseBtn");
        var mainBrowse = browser.Page.Locator("#ConfigFilePathBrowseBtn");
        if (!edit)
        {
            await Assertions.Expect(browse).Not.ToBeVisibleAsync();
            await Assertions.Expect(mainBrowse).Not.ToBeVisibleAsync();
            await browser.Page.GetByTestId("server-select").SelectOptionAsync(MapRotationAssignmentScenario.PermittedServerId.ToString());
        }

        await Assertions.Expect(browse).ToBeVisibleAsync();
        await Assertions.Expect(mainBrowse).ToBeVisibleAsync();
        var pointer = browser.Page.Locator("#AacpConfigFilePathHelp");
        await Assertions.Expect(pointer).ToBeVisibleAsync();
        await Assertions.Expect(pointer).ToContainTextAsync("scr_aacp_maps_1");
        await Assertions.Expect(pointer).ToContainTextAsync("sv_maprotation");
        var main = browser.Page.GetByLabel("Config File", new() { Exact = true });
        await main.FillAsync("/configs/original.cfg");
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browse.ClickAsync(),
            response => response.Request.Method == "GET" && new Uri(response.Url).AbsolutePath == endpoint);
        Assert.Contains("purpose=map-rotation-assignment", new Uri(response.Url).Query, StringComparison.Ordinal);
        await Assertions.Expect(browser.Page.Locator("#fileBrowseModal")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#fileBrowseListingBody tr").Filter(new() { HasText = "server.log" }))
            .ToHaveClassAsync("opacity-50");
        await browser.Page.Locator("#fileBrowseListingBody tr").Filter(new() { HasText = "advancedacp.cfg" }).ClickAsync();
        await browser.Page.Locator("#fileBrowseSelectBtn").ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#fileBrowseModal")).Not.ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.GetByLabel("AACP config file", new() { Exact = true }))
            .ToHaveValueAsync("/configs/advancedacp.cfg");
        await Assertions.Expect(main).ToHaveValueAsync("/configs/original.cfg");
        await Assertions.Expect(browser.Page.GetByLabel("Config Variable", new() { Exact = true })).ToHaveValueAsync("sv_maprotation");
        await browser.Page.RunAndWaitForResponseAsync(
            () => mainBrowse.ClickAsync(),
            response => response.Request.Method == "GET" && new Uri(response.Url).AbsolutePath == endpoint);
        await browser.Page.Locator("#fileBrowseListingBody tr").Filter(new() { HasText = "mapcontrol.cfg" }).ClickAsync();
        await browser.Page.Locator("#fileBrowseSelectBtn").ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#fileBrowseModal")).Not.ToBeVisibleAsync();
        await Assertions.Expect(main).ToHaveValueAsync("/configs/mapcontrol.cfg");
        await Assertions.Expect(browser.Page.GetByLabel("AACP config file", new() { Exact = true }))
            .ToHaveValueAsync("/configs/advancedacp.cfg");
        if (!edit)
        {
            await browser.Page.GetByTestId("server-select").SelectOptionAsync("");
            await Assertions.Expect(browse).Not.ToBeVisibleAsync();
            await Assertions.Expect(mainBrowse).Not.ToBeVisibleAsync();
        }

        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentForms_WithoutFileTransport_KeepManualAacpEntryWithoutBrowse(bool edit)
    {
        var create = new MapRotationAssignmentScenario();
        var recovery = new MapRotationRecoveryScenario(combined: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer,
            edit ? recovery.ConfigureServices : create.ConfigureServices);
        var url = edit ? $"/MapRotations/EditAssignment/{recovery.AssignmentId}"
            : $"/MapRotations/CreateAssignment?mapRotationId={MapRotationAssignmentScenario.RotationId}";
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, url).AbsoluteUri);
        await browser.Page.GetByLabel("Also update AACP map picker", new() { Exact = true }).CheckAsync();
        await Assertions.Expect(browser.Page.Locator("#AacpConfigFilePathBrowseBtn")).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.Locator("#ConfigFilePathBrowseBtn")).ToHaveCountAsync(0);
        var path = browser.Page.GetByLabel("AACP config file", new() { Exact = true });
        await path.FillAsync("/configs/advancedacp.cfg");
        await Assertions.Expect(path).ToHaveValueAsync("/configs/advancedacp.cfg");
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
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Never);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task CombinedEdit_RejectedVariableChange_KeepsAdvancedExplanationVisible()
    {
        var scenario = new MapRotationRecoveryScenario(combined: true);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.MapRotationDeployer, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapRotations/EditAssignment/{scenario.AssignmentId}").AbsoluteUri);
        var advanced = browser.Page.Locator("#assignment-advanced");
        await Assertions.Expect(advanced.Locator("[data-valmsg-for='ConfigVariableName']")).Not.ToBeVisibleAsync();
        await advanced.Locator("summary").ClickAsync();
        await browser.Page.GetByLabel("Config Variable", new() { Exact = true }).FillAsync("sv_maprotation_low");
        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save Changes", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == $"/MapRotations/EditAssignment/{scenario.AssignmentId}");
        Assert.Equal(200, response.Status);
        var explanation = advanced.Locator("[data-valmsg-for='ConfigVariableName']");
        await Assertions.Expect(explanation).ToBeVisibleAsync();
        await Assertions.Expect(explanation).ToContainTextAsync("sv_maprotation");
        await Assertions.Expect(advanced.Locator("#ConfigVariableName")).ToHaveValueAsync("sv_maprotation_low");
        scenario.Repository.Verify(x => x.MapRotations.V1.UpdateServerAssignment(
            It.IsAny<UpdateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()), Times.Never);
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
        await Assertions.Expect(browser.Page.Locator(".alert-danger")).Not.ToContainTextAsync("Synthetic verification error on active configuration");
        await browser.Page.Locator("#assignment-details > summary").ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#assignment-details")).ToContainTextAsync("Synthetic verification error on active configuration");
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
        await Assertions.Expect(browser.Page.Locator(".pending-bootstrap-row .operation-error"))
            .ToContainTextAsync("Expand technical details");
        await browser.Page.Locator(".pending-bootstrap-row details > summary").ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".pending-bootstrap-row .operation-diagnostics")).ToHaveTextAsync("Synthetic bootstrap failure");
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
