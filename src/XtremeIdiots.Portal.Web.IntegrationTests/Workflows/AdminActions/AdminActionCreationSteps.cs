using Microsoft.Playwright;
using Moq;
using Reqnroll;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.AdminActions;

[Binding]
public sealed class AdminActionCreationSteps
{
    private BrowserFixture? browser;
    private string? profile;
    private IResponse? response;

    [Given("a successful admin action scenario for a senior admin")]
    public void GivenASuccessfulAdminActionScenarioForASeniorAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.SeniorAdmin, createSucceeds: true);
    }

    [Given("a successful admin action scenario for a moderator")]
    public void GivenASuccessfulAdminActionScenarioForAModerator()
    {
        ConfigureScenario(TestPrincipalProfiles.Moderator, createSucceeds: true);
    }

    [Given("a failing repository admin action scenario for a senior admin")]
    public void GivenAFailingRepositoryAdminActionScenarioForASeniorAdmin()
    {
        ConfigureScenario(TestPrincipalProfiles.SeniorAdmin, createSucceeds: false);
    }

    [When("the senior admin creates a ban for repeated abusive behaviour")]
    public async Task WhenTheSeniorAdminCreatesABanForRepeatedAbusiveBehaviour()
    {
        await SubmitCreateFormAsync(AdminActionType.Ban, "Repeated abusive behaviour");
    }

    [When("the moderator creates an observation for disruptive play")]
    public async Task WhenTheModeratorCreatesAnObservationForDisruptivePlay()
    {
        await SubmitCreateFormAsync(AdminActionType.Observation, "Observed disruptive play");
    }

    [When("the senior admin submits the {string} reason case for a ban")]
    public async Task WhenTheSeniorAdminSubmitsTheReasonCaseForABan(string reasonCase)
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(CreateUrl(AdminActionType.Ban));
        await Browser.Page.EvaluateAsync("reason => $('#summernote').summernote('code', reason)", ResolveReasonCase(reasonCase));
        response = await SubmitAndCaptureResponseAsync();
    }

    [When("the moderator navigates directly to the ban form")]
    public async Task WhenTheModeratorNavigatesDirectlyToTheBanForm()
    {
        await StartBrowserAsync();
        response = await Browser.Page.GotoAsync(CreateUrl(AdminActionType.Ban));
    }

    [When("the moderator forges a ban submission from the observation form")]
    public async Task WhenTheModeratorForgesABanSubmissionFromTheObservationForm()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(CreateUrl(AdminActionType.Observation));
        await Browser.Page.Locator("input[name='Type']").EvaluateAsync("element => element.value = 'Ban'");
        await FillReasonAsync("Forged ban reason");
        response = await SubmitAndCaptureResponseAsync();
    }

    [When("the senior admin submits a valid ban")]
    public async Task WhenTheSeniorAdminSubmitsAValidBan()
    {
        await StartBrowserAsync();
        await Browser.Page.GotoAsync(CreateUrl(AdminActionType.Ban));
        await FillReasonAsync("Valid reason that cannot be saved");
        response = await SubmitAndCaptureResponseAsync();
    }

    [Then("the ban command should contain the expected details")]
    public void ThenTheBanCommandShouldContainTheExpectedDetails()
    {
        var command = Assert.Single(Scenario.CreatedAdminActions);
        Assert.Equal(Scenario.PlayerId, command.PlayerId);
        Assert.Equal(AdminActionType.Ban, command.Type);
        Assert.Equal("<p>Repeated abusive behaviour</p>", command.Text);
        Assert.Equal("12345", command.AdminId);
        Assert.Equal(123456, command.ForumTopicId);
    }

    [Then("a ban notification should be dispatched")]
    public void ThenABanNotificationShouldBeDispatched()
    {
        var notification = Assert.Single(Scenario.Notifications);
        Assert.Equal(Scenario.PlayerId, notification.PlayerId);
        Assert.Equal(AdminActionType.Ban, notification.ActionType);
        Assert.Equal("WorkflowPlayer", notification.PlayerName);
    }

    [Then("the observation command should contain the expected details")]
    public void ThenTheObservationCommandShouldContainTheExpectedDetails()
    {
        var command = Assert.Single(Scenario.CreatedAdminActions);
        Assert.Equal(Scenario.PlayerId, command.PlayerId);
        Assert.Equal(AdminActionType.Observation, command.Type);
        Assert.Equal("<p>Observed disruptive play</p>", command.Text);
        Assert.Equal("12345", command.AdminId);
        Assert.Equal(123456, command.ForumTopicId);
    }

    [Then("an admin action notification should be dispatched")]
    public void ThenAnAdminActionNotificationShouldBeDispatched()
    {
        var notification = Assert.Single(Scenario.Notifications);
        Assert.Equal(Scenario.PlayerId, notification.PlayerId);
        Assert.Equal(AdminActionType.Observation, notification.ActionType);
        Assert.Equal("WorkflowPlayer", notification.PlayerName);
    }

    [Then("the admin action reason validation should be displayed")]
    public async Task ThenTheAdminActionReasonValidationShouldBeDisplayed()
    {
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await Assertions.Expect(Browser.Page.GetByText("You must enter a reason for the admin action", new()
        {
            Exact = true,
        })).ToBeVisibleAsync();
    }

    [Then("the ban form should be denied")]
    public async Task ThenTheBanFormShouldBeDenied()
    {
        Assert.NotNull(response);
        Assert.EndsWith("/Errors/Display/401", Browser.Page.Url, StringComparison.Ordinal);
        await Assertions.Expect(Browser.Page.GetByTestId("admin-action-create-form")).ToHaveCountAsync(0);
    }

    [Then("the forged ban submission should be denied")]
    public async Task ThenTheForgedBanSubmissionShouldBeDenied()
    {
        Assert.NotNull(response);
        Assert.Equal(302, response.Status);
        await Browser.Page.WaitForURLAsync("**/Errors/Display/401");
        VerifyTopicCreation(Times.Never());
    }

    [Then("no admin action side effects should be recorded")]
    public void ThenNoAdminActionSideEffectsShouldBeRecorded()
    {
        Assert.Empty(Scenario.CreatedAdminActions);
        Assert.Empty(Scenario.Notifications);
        VerifyTopicCreation(Times.Never());
    }

    [Then("the partial failure guidance should be displayed")]
    public async Task ThenThePartialFailureGuidanceShouldBeDisplayed()
    {
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Single(Scenario.CreatedAdminActions);
        await Assertions.Expect(Browser.Page.GetByText(
            "The discussion topic was created, but the admin action could not be saved. Remove the discussion topic before retrying.",
            new() { Exact = true })).ToBeVisibleAsync();
    }

    [Then("the discussion topic should have been created once")]
    public void ThenTheDiscussionTopicShouldHaveBeenCreatedOnce()
    {
        VerifyTopicCreation(Times.Once());
    }

    [Then("no admin action notification should be dispatched")]
    public void ThenNoAdminActionNotificationShouldBeDispatched()
    {
        Assert.Empty(Scenario.Notifications);
    }

    [Then("the admin action browser should report no errors")]
    public void ThenTheAdminActionBrowserShouldReportNoErrors()
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

    private AdminActionScenario Scenario { get => field ?? throw new InvalidOperationException("The admin action scenario has not been configured."); set; }

    private void ConfigureScenario(string authenticationProfile, bool createSucceeds)
    {
        profile = authenticationProfile;
        Scenario = new AdminActionScenario(createSucceeds: createSucceeds);
    }

    private string CreateUrl(AdminActionType actionType)
    {
        return new Uri(
            Browser.Host.BaseAddress,
            $"/AdminActions/Create?id={Scenario.PlayerId}&adminActionType={actionType}").AbsoluteUri;
    }

    private async Task StartBrowserAsync()
    {
        browser = await BrowserFixture.CreateAsync(
            profile ?? throw new InvalidOperationException("The authentication profile has not been configured."),
            Scenario.ConfigureServices);
    }

    private async Task SubmitCreateFormAsync(AdminActionType actionType, string reason)
    {
        await StartBrowserAsync();
        var formResponse = await Browser.Page.GotoAsync(CreateUrl(actionType));
        Assert.NotNull(formResponse);
        Assert.True(formResponse.Ok);
        await Assertions.Expect(Browser.Page.GetByTestId("admin-action-create-form")).ToBeVisibleAsync();
        await FillReasonAsync(reason);
        var analyticsResponse = await Browser.Page.RunAndWaitForResponseAsync(
            async () =>
            {
                var commandResponse = await SubmitAndCaptureResponseAsync();
                Assert.Equal(302, commandResponse.Status);
                await Browser.Page.WaitForURLAsync(new Uri(Browser.Host.BaseAddress, $"/Players/Details/{Scenario.PlayerId}").AbsoluteUri);
            },
            browserResponse => browserResponse.Request.Method == "GET" &&
                new Uri(browserResponse.Url).AbsolutePath == $"/api/Analytics/player/{Scenario.PlayerId}/timeseries");
        Assert.True(analyticsResponse.Ok);
        await Assertions.Expect(Browser.Page.Locator("#analytics-embed-player-summary")).Not.ToBeEmptyAsync();
    }

    private async Task FillReasonAsync(string reason)
    {
        await Browser.Page.Locator(".note-editable").FillAsync(reason);
        // Summernote updates the submitted textarea asynchronously after editing.
        await Assertions.Expect(Browser.Page.GetByTestId("admin-action-create-reason"))
            .ToHaveValueAsync(new Regex(Regex.Escape(reason)));
    }

    private async Task<IResponse> SubmitAndCaptureResponseAsync()
    {
        return await Browser.Page.RunAndWaitForResponseAsync(
            () => Browser.Page.GetByTestId("admin-action-create-submit").ClickAsync(),
            browserResponse => browserResponse.Request.Method == "POST" &&
                new Uri(browserResponse.Url).AbsolutePath == "/AdminActions/Create");
    }

    private void VerifyTopicCreation(Times times)
    {
        Scenario.AdminActionTopics.Verify(
            topics => topics.CreateTopicForAdminAction(
                It.IsAny<AdminActionType>(),
                It.IsAny<GameType>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            times);
    }

    private static string ResolveReasonCase(string reasonCase)
    {
        return reasonCase switch
        {
            "short text" => "no",
            "style block" => "<style>.hidden-content { display: none; }</style>",
            "hidden attribute" => "<p hidden>hidden reason</p>",
            "quoted display none" => "<p style=\"display: none\">hidden reason</p>",
            "unquoted display none" => "<p style=display:none>hidden reason</p>",
            "unquoted visibility hide" => "<p style=visibility:hidden>hidden reason</p>",
            "zero-width characters" => "\u200B\u200C\u200D",
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCase), reasonCase, "Unknown reason case."),
        };
    }
}
