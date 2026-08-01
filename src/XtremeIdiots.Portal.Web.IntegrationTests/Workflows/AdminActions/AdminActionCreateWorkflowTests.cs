using Moq;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.AdminActions;

public class AdminActionCreateWorkflowTests
{
    [Fact]
    public async Task SeniorAdmin_CreatesBanAndRecordsDownstreamCommands()
    {
        var scenario = new AdminActionScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.SeniorAdmin,
            scenario.ConfigureServices);

        await SubmitCreateFormAsync(fixture, scenario, AdminActionType.Ban, "Repeated abusive behaviour");

        var command = Assert.Single(scenario.CreatedAdminActions);
        Assert.Equal(scenario.PlayerId, command.PlayerId);
        Assert.Equal(AdminActionType.Ban, command.Type);
        Assert.Equal("<p>Repeated abusive behaviour</p>", command.Text);
        Assert.Equal("12345", command.AdminId);
        Assert.Equal(123456, command.ForumTopicId);

        var notification = Assert.Single(scenario.Notifications);
        Assert.Equal(scenario.PlayerId, notification.PlayerId);
        Assert.Equal(AdminActionType.Ban, notification.ActionType);
        Assert.Equal("WorkflowPlayer", notification.PlayerName);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Moderator_CreatesObservationAndRecordsDownstreamCommands()
    {
        var scenario = new AdminActionScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.Moderator,
            scenario.ConfigureServices);

        await SubmitCreateFormAsync(fixture, scenario, AdminActionType.Observation, "Observed disruptive play");

        var command = Assert.Single(scenario.CreatedAdminActions);
        Assert.Equal(AdminActionType.Observation, command.Type);
        Assert.Equal("<p>Observed disruptive play</p>", command.Text);
        Assert.Equal(123456, command.ForumTopicId);
        Assert.Single(scenario.Notifications);
        fixture.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData("no")]
    [InlineData("<style>.hidden-content { display: none; }</style>")]
    [InlineData("<p hidden>hidden reason</p>")]
    [InlineData("<p style=\"display: none\">hidden reason</p>")]
    [InlineData("<p style=display:none>hidden reason</p>")]
    [InlineData("<p style=visibility:hidden>hidden reason</p>")]
    [InlineData("\u200B\u200C\u200D")]
    public async Task InvisibleOrShortReason_RerendersFormWithValidationErrorAndNoCommands(string reason)
    {
        var scenario = new AdminActionScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.SeniorAdmin,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(CreateUrl(fixture, scenario, AdminActionType.Ban));
        await fixture.Page.EvaluateAsync("reason => $('#summernote').summernote('code', reason)", reason);

        var responseTask = fixture.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath == "/AdminActions/Create");
        await fixture.Page.GetByTestId("admin-action-create-submit").ClickAsync();
        var response = await responseTask;

        Assert.Equal(200, response.Status);
        Assert.True(await fixture.Page.GetByText("You must enter a reason for the admin action", new()
        {
            Exact = true,
        }).IsVisibleAsync());
        Assert.Empty(scenario.CreatedAdminActions);
        Assert.Empty(scenario.Notifications);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Moderator_CannotOpenBanCreateFormDirectly()
    {
        var scenario = new AdminActionScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.Moderator,
            scenario.ConfigureServices);

        var response = await fixture.Page.GotoAsync(CreateUrl(fixture, scenario, AdminActionType.Ban));

        Assert.NotNull(response);
        Assert.EndsWith("/Errors/Display/401", fixture.Page.Url, StringComparison.Ordinal);
        Assert.False(await fixture.Page.GetByTestId("admin-action-create-form").IsVisibleAsync());
        Assert.Empty(scenario.CreatedAdminActions);
        Assert.Empty(scenario.Notifications);
    }

    [Fact]
    public async Task Moderator_CannotForgeBanCreatePost()
    {
        var scenario = new AdminActionScenario();
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.Moderator,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(CreateUrl(fixture, scenario, AdminActionType.Observation));
        await fixture.Page.Locator("input[name='Type']").EvaluateAsync("element => element.value = 'Ban'");
        await fixture.Page.Locator(".note-editable").FillAsync("Forged ban reason");

        var responseTask = fixture.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath == "/AdminActions/Create");
        await fixture.Page.GetByTestId("admin-action-create-submit").ClickAsync();
        var response = await responseTask;

        Assert.Equal(302, response.Status);
        await fixture.Page.WaitForURLAsync("**/Errors/Display/401");
        Assert.Empty(scenario.CreatedAdminActions);
        Assert.Empty(scenario.Notifications);
        scenario.AdminActionTopics.Verify(
            topics => topics.CreateTopicForAdminAction(
                It.IsAny<AdminActionType>(),
                It.IsAny<GameType>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RepositoryFailure_ReportsPartialFailureWithoutDispatchingNotification()
    {
        var scenario = new AdminActionScenario(createSucceeds: false);
        await using var fixture = await BrowserFixture.CreateAsync(
            TestPrincipalProfiles.SeniorAdmin,
            scenario.ConfigureServices);
        await fixture.Page.GotoAsync(CreateUrl(fixture, scenario, AdminActionType.Ban));
        await fixture.Page.Locator(".note-editable").FillAsync("Valid reason that cannot be saved");

        var responseTask = fixture.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath == "/AdminActions/Create");
        await fixture.Page.GetByTestId("admin-action-create-submit").ClickAsync();
        var response = await responseTask;

        Assert.Equal(200, response.Status);
        Assert.Single(scenario.CreatedAdminActions);
        Assert.Empty(scenario.Notifications);
        scenario.AdminActionTopics.Verify(
            topics => topics.CreateTopicForAdminAction(
                It.IsAny<AdminActionType>(),
                It.IsAny<GameType>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.True(await fixture.Page.GetByText(
            "The discussion topic was created, but the admin action could not be saved. Remove the discussion topic before retrying.",
            new() { Exact = true }).IsVisibleAsync());
        fixture.AssertNoBrowserErrors();
    }

    private static string CreateUrl(BrowserFixture fixture, AdminActionScenario scenario, AdminActionType actionType)
    {
        return new Uri(
            fixture.Host.BaseAddress,
            $"/AdminActions/Create?id={scenario.PlayerId}&adminActionType={actionType}").AbsoluteUri;
    }

    private async static Task SubmitCreateFormAsync(
        BrowserFixture fixture,
        AdminActionScenario scenario,
        AdminActionType actionType,
        string reason)
    {
        var response = await fixture.Page.GotoAsync(CreateUrl(fixture, scenario, actionType));

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.True(await fixture.Page.GetByTestId("admin-action-create-form").IsVisibleAsync());

        var editor = fixture.Page.Locator(".note-editable");
        Assert.True(await editor.IsVisibleAsync());
        await editor.FillAsync(reason);

        var commandResponseTask = fixture.Page.WaitForResponseAsync(browserResponse =>
            browserResponse.Request.Method == "POST" &&
            new Uri(browserResponse.Url).AbsolutePath == "/AdminActions/Create");
        await fixture.Page.GetByTestId("admin-action-create-submit").ClickAsync();
        var commandResponse = await commandResponseTask;

        Assert.Equal(302, commandResponse.Status);
        await fixture.Page.WaitForURLAsync("**/Players/Details**");
    }
}
