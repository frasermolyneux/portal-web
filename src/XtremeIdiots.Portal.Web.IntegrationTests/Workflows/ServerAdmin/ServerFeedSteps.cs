using Microsoft.Playwright;
using Reqnroll;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Binding]
public sealed class ServerFeedSteps
{
    private const string ChatOneId = "chat:11111111-2222-3333-4444-555555555555";
    private const string ChatTwoId = "chat:22222222-3333-4444-5555-666666666666";
    private const string EventOneId = "event:33333333-4444-5555-6666-777777777777";
    private const string EventTwoId = "event:44444444-5555-6666-7777-888888888888";
    private const string HostileFeedText = "<img data-testid='feed-xss' src='/missing-feed-image' onerror='console.error(\"feed-xss\")'>";
    private const string HostileEventType = "MapChange\" data-testid=\"feed-event-xss\" onmouseover=\"console.error('feed-event-xss')";

    private BrowserFixture? browser;
    private bool initiallyBackgrounded;
    private int requestCountBeforeAction;

    [Given("a server feed with chat and event items")]
    public void GivenChatAndEventItems()
    {
        Configure();
        Scenario.QueueResponse(
            items:
            [
                ServerFeedScenario.Event(EventOneId, HostileEventType, System.Text.Json.JsonSerializer.Serialize(new { slot = 7 })),
                ServerFeedScenario.Chat(ChatOneId, HostileFeedText),
            ]);
    }

    [Given("a server feed with multiple event types")]
    public void GivenMultipleEventTypes()
    {
        Configure();
        Scenario.QueueResponse(
            items:
            [
                ServerFeedScenario.Event(EventOneId, "PlayerConnected"),
                ServerFeedScenario.Event(EventTwoId, "MapChange"),
            ]);
    }

    [Given("a server feed with one chat item")]
    public void GivenOneChatItem()
    {
        Configure();
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat(ChatOneId, "Initial chat")]);
    }

    [Given("an overrun server feed response")]
    public void GivenOverrunResponse()
    {
        Configure();
        Scenario.QueueResponse(overrun: true, items: [ServerFeedScenario.Chat(ChatOneId, "High volume chat")]);
    }

    [Given("the page is initially backgrounded")]
    public void GivenPageInitiallyBackgrounded()
    {
        initiallyBackgrounded = true;
    }

    [When("the user opens the server feed")]
    public async Task WhenUserOpensFeed()
    {
        await OpenFeedAsync();
    }

    [When("the user filters events by {string}")]
    public async Task WhenUserFiltersEvents(string filter)
    {
        await Browser.Page.Locator("#sd-feedEventFilter").FillAsync(filter);
    }

    [When("the user disables event feed entries")]
    public async Task WhenUserDisablesEvents()
    {
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat(ChatTwoId, "Chat only")]);
        var responseTask = WaitForNextFeedResponseAsync();
        await Browser.Page.Locator("#sd-feedToggleEvents").UncheckAsync();
        await responseTask;
    }

    [When("the user pauses feed refresh")]
    public async Task WhenUserPausesRefresh()
    {
        await Browser.Page.Locator("#sd-toggleRefresh").ClickAsync();
    }

    [When("a new chat item is refreshed")]
    public async Task WhenNewChatIsRefreshed()
    {
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat(ChatTwoId, "Buffered chat")]);
        await RefreshAsync();
    }

    [When("the user resumes feed refresh")]
    public async Task WhenUserResumesRefresh()
    {
        await Browser.Page.Locator("#sd-toggleRefresh").ClickAsync();
    }

    [When("the same chat item is refreshed")]
    public async Task WhenSameChatIsRefreshed()
    {
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat(ChatOneId, "Initial chat")]);
        await RefreshAsync();
    }

    [When("an empty incremental response is refreshed")]
    public async Task WhenEmptyIncrementRefreshed()
    {
        Scenario.QueueResponse(items: []);
        await RefreshAsync();
    }

    [When("another incremental refresh is requested")]
    public async Task WhenAnotherIncrementRequested()
    {
        Scenario.QueueResponse(items: []);
        await RefreshAsync();
    }

    [When("another overrun response is refreshed")]
    public async Task WhenAnotherOverrunIsRefreshed()
    {
        Scenario.QueueResponse(overrun: true, items: [ServerFeedScenario.Chat(ChatTwoId, "More volume")]);
        await RefreshAsync();
    }

    [When("an overrun forced reload is requested")]
    public async Task WhenOverrunForcedReloadRequested()
    {
        Scenario.QueueResponse(overrun: true, items: [ServerFeedScenario.Chat(ChatTwoId, "Reloaded volume")]);
        var responseTask = WaitForNextFeedResponseAsync();
        await Browser.Page.EvaluateAsync("ServerFeed.forceReload()");
        await responseTask;
    }

    [When("the page is backgrounded and refresh is requested")]
    public async Task WhenPageBackgroundedAndRefreshed()
    {
        requestCountBeforeAction = Scenario.Requests.Count;
        await Browser.Page.EvaluateAsync("Object.defineProperty(document, 'hidden', { configurable: true, value: true }); document.dispatchEvent(new Event('visibilitychange')); ServerFeed.refresh();");
        await Browser.Page.EvaluateAsync("new Promise(resolve => setTimeout(resolve, 100))");
    }

    [When("the page becomes visible and refresh is requested")]
    public async Task WhenPageVisibleAndRefreshed()
    {
        requestCountBeforeAction = Scenario.Requests.Count;
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat(ChatTwoId, "Visible refresh")]);
        var responseTask = WaitForNextFeedResponseAsync();
        await Browser.Page.EvaluateAsync("Object.defineProperty(document, 'hidden', { configurable: true, value: false }); document.dispatchEvent(new Event('visibilitychange')); ServerFeed.refresh();");
        await responseTask;
    }

    [When("two refreshes are requested while one is active")]
    public async Task WhenOverlappingRefreshesRequested()
    {
        requestCountBeforeAction = Scenario.Requests.Count;
        Scenario.QueueResponse(250, items: [ServerFeedScenario.Chat(ChatTwoId, "Delayed refresh")]);
        var responseTask = WaitForNextFeedResponseAsync();
        await Browser.Page.EvaluateAsync("ServerFeed.refresh(); ServerFeed.refresh();");
        await responseTask;
    }

    [When("the server feed is disposed and refresh is requested")]
    public async Task WhenDisposedAndRefreshed()
    {
        requestCountBeforeAction = Scenario.Requests.Count;
        await Browser.Page.EvaluateAsync("ServerFeed.dispose(); ServerFeed.refresh();");
        await Browser.Page.EvaluateAsync("new Promise(resolve => setTimeout(resolve, 100))");
    }

    [When("a forced reload supersedes a delayed refresh")]
    public async Task WhenForcedReloadSupersedesDelayedRefresh()
    {
        requestCountBeforeAction = Scenario.Requests.Count;
        Scenario.QueueResponse(300, items: [ServerFeedScenario.Chat(ChatTwoId, "Aborted item")]);
        Scenario.QueueResponse(items: [ServerFeedScenario.Chat("chat:55555555-6666-7777-8888-999999999999", "Replacement item")]);
        await Browser.Page.EvaluateAsync("ServerFeed.refresh(); setTimeout(function () { ServerFeed.forceReload(); }, 25);");
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).ToContainTextAsync("Replacement item");
    }

    [Then("the feed should show the chat and event items")]
    public async Task ThenFeedShowsChatAndEvent()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems [data-source='chat']")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems [data-source='event']")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).ToContainTextAsync(HostileFeedText);
    }

    [Then("the feed should contain no injected image")]
    public async Task ThenNoInjectedImage()
    {
        await Assertions.Expect(Browser.Page.GetByTestId("feed-xss")).ToHaveCountAsync(0);
        await Assertions.Expect(Browser.Page.GetByTestId("feed-event-xss")).ToHaveCountAsync(0);
    }

    [Then("only the map event should be visible")]
    public async Task ThenOnlyMapEventVisible()
    {
        var events = Browser.Page.Locator("#sd-feedItems [data-source='event']");
        await Assertions.Expect(events).ToHaveCountAsync(1);
        await Assertions.Expect(events).ToContainTextAsync("MapChange");
    }

    [Then("the latest feed request should exclude events")]
    public void ThenLatestRequestExcludesEvents()
    {
        Assert.Contains("includeEvents=false", Scenario.Requests.Last().Query, StringComparison.Ordinal);
    }

    [Then("the latest feed request should not contain a cursor")]
    public void ThenLatestRequestHasNoCursor()
    {
        Assert.DoesNotContain("lastSeenTimestampUtc", Scenario.Requests.Last().Query, StringComparison.Ordinal);
        Assert.DoesNotContain("lastSeenSourceType", Scenario.Requests.Last().Query, StringComparison.Ordinal);
        Assert.DoesNotContain("lastSeenItemId", Scenario.Requests.Last().Query, StringComparison.Ordinal);
        Assert.DoesNotContain("lastChatMessageId", Scenario.Requests.Last().Query, StringComparison.Ordinal);
        Assert.DoesNotContain("lastEventId", Scenario.Requests.Last().Query, StringComparison.Ordinal);
    }

    [Then("the latest feed request should retain all cursor values")]
    public void ThenLatestRequestRetainsCursor()
    {
        var query = Scenario.Requests.Last().Query;
        Assert.Contains("lastSeenTimestampUtc=", query, StringComparison.Ordinal);
        Assert.Contains("lastSeenSourceType=", query, StringComparison.Ordinal);
        Assert.Contains("lastSeenItemId=", query, StringComparison.Ordinal);
        Assert.Contains("lastChatMessageId=", query, StringComparison.Ordinal);
        Assert.Contains("lastEventId=", query, StringComparison.Ordinal);
    }

    [Then("no event feed item should be visible")]
    public async Task ThenNoEventItemVisible()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems [data-source='event']")).ToHaveCountAsync(0);
    }

    [Then("the feed should report {string} pending item")]
    public async Task ThenPendingCountReported(string count)
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedPendingCount")).ToContainTextAsync(count);
        await Assertions.Expect(Browser.Page.Locator("#sd-feedPendingCount")).ToBeVisibleAsync();
    }

    [Then("the new chat item should not be visible")]
    public async Task ThenNewChatNotVisible()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).Not.ToContainTextAsync("Buffered chat");
    }

    [Then("the new chat item should be visible")]
    public async Task ThenNewChatVisible()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).ToContainTextAsync("Buffered chat");
    }

    [Then("the feed should contain one chat item")]
    public async Task ThenOneChatItem()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems [data-source='chat']")).ToHaveCountAsync(1);
    }

    [Then("the high-volume indicator should be visible")]
    public async Task ThenHighVolumeIndicatorVisible()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedOverrunIndicator")).ToBeVisibleAsync();
    }

    [Then("one high-volume notice should be visible")]
    public async Task ThenOneHighVolumeNotice()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems [data-source='system']")).ToHaveCountAsync(1);
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).ToContainTextAsync("Feed volume is high");
    }

    [Then("no background feed request should be sent")]
    [Then("no disposed feed request should be sent")]
    public void ThenNoFeedRequestSent()
    {
        Assert.Equal(requestCountBeforeAction, Scenario.Requests.Count);
    }

    [Then("one visible feed request should be sent")]
    [Then("one overlapping feed request should be sent")]
    public void ThenOneFeedRequestSent()
    {
        Assert.Equal(requestCountBeforeAction + 1, Scenario.Requests.Count);
    }

    [Then("no initial feed request should be sent")]
    public void ThenNoInitialFeedRequestSent()
    {
        Assert.Empty(Scenario.Requests);
    }

    [Then("two supersession feed requests should be sent")]
    public void ThenTwoSupersessionRequestsSent()
    {
        Assert.Equal(requestCountBeforeAction + 2, Scenario.Requests.Count);
    }

    [Then("only the replacement feed item should be visible")]
    public async Task ThenOnlyReplacementItemVisible()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).ToContainTextAsync("Replacement item");
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).Not.ToContainTextAsync("Aborted item");
    }

    [Then("the server feed browser should report no errors")]
    public void ThenBrowserReportsNoErrors()
    {
        Browser.AssertNoBrowserErrors();
    }

    [Then("the superseded feed request should be the only browser failure")]
    public void ThenSupersededRequestIsOnlyFailure()
    {
        Browser.AssertOnlyExpectedFailedRequest(
            "GET",
            $"/ServerAdmin/GetServerFeed/{Scenario.GameServerId}");
    }

    [AfterScenario]
    public async Task DisposeBrowserAsync()
    {
        if (browser is not null)
            await browser.DisposeAsync();
    }

    private BrowserFixture Browser => browser ?? throw new InvalidOperationException("Browser not started.");
    private ServerFeedScenario Scenario { get => field ?? throw new InvalidOperationException("Scenario not configured."); set; }
    private string ServerDetailUrl => new Uri(Browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{Scenario.GameServerId}").AbsoluteUri;

    private void Configure()
    {
        Scenario = new ServerFeedScenario();
    }

    private async Task OpenFeedAsync()
    {
        browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, Scenario.ConfigureServices);
        await Browser.Page.RouteAsync("**/ServerAdmin/GetServerFeed/**", HandleFeedRouteAsync);
        if (initiallyBackgrounded)
        {
            await Browser.Page.AddInitScriptAsync("Object.defineProperty(document, 'hidden', { configurable: true, get: function () { return true; } });");
        }

        var response = await Browser.Page.GotoAsync(ServerDetailUrl);
        Assert.NotNull(response);
        Assert.True(response.Ok);
        if (!initiallyBackgrounded)
            await WaitForInitialFeedAsync();
        else
            await Browser.Page.EvaluateAsync("new Promise(resolve => setTimeout(resolve, 100))");
    }

    private async Task HandleFeedRouteAsync(IRoute route)
    {
        Scenario.Requests.Enqueue(new Uri(route.Request.Url));
        if (!Scenario.Plans.TryDequeue(out var plan))
            throw new InvalidOperationException("No queued server feed response.");

        try
        {
            if (plan.DelayMilliseconds > 0)
                await Task.Delay(plan.DelayMilliseconds);

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = plan.Json,
            });
        }
        catch (PlaywrightException) when (route.Request.Failure is not null)
        {
        }
    }

    private async Task WaitForInitialFeedAsync()
    {
        await Assertions.Expect(Browser.Page.Locator("#sd-feedItems")).Not.ToContainTextAsync("Loading server feed");
        Assert.NotEmpty(Scenario.Requests);
    }

    private async Task RefreshAsync()
    {
        var responseTask = WaitForNextFeedResponseAsync();
        await Browser.Page.EvaluateAsync("ServerFeed.refresh()");
        await responseTask;
    }

    private Task<IResponse> WaitForNextFeedResponseAsync()
    {
        return Browser.Page.WaitForResponseAsync(response =>
            new Uri(response.Url).AbsolutePath.Equals($"/ServerAdmin/GetServerFeed/{Scenario.GameServerId}", StringComparison.Ordinal));
    }
}
