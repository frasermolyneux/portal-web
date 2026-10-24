using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Redirect.Api.Abstractions.V1;
using XtremeIdiots.Redirect.Api.Client.Testing;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "Browser")]
public sealed class NativeMapDownloadsBrowserTests
{
    private const string FilePath = "redirect/cod4/usermaps/mp_test/map.ff";
    private const string DirectUrl = "https://maps.test.invalid/v1/downloads/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData(1440, true)]
    [InlineData(390, false)]
    public async Task FolderDetail_OnlyShowsRelevantActions_AndFitsDesktopAndMobile(int width, bool authenticated)
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateArchive", RedirectDtoFactory.CreateArchive(status: "complete"));
        await using var browser = await BrowserFixture.CreateAsync(authenticated ? TestPrincipalProfiles.GameAdmin : null,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.SetViewportSizeAsync(width, 900);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#downloadEntries [role=listitem]")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("#downloadHeading")).ToHaveTextAsync("mp_test");
        await Assertions.Expect(browser.Page.Locator("#downloadSearch")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadPagination")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadSelectionBar")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadActivity")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("[data-select-path]")).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Open in Maps Manager" }))
            .ToHaveAttributeAsync("href", "https://maps.xtremeidiots.com/?game=cod4&map=mp_test");
        Assert.True(await browser.Page.Locator("#mapDownloads").EvaluateAsync<bool>(
            "root => root.scrollWidth <= root.clientWidth && root.getBoundingClientRect().width <= 1120"));
        if (authenticated)
        {
            var zip = browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" });
            await Assertions.Expect(zip).ToHaveCountAsync(1);
            await zip.ClickAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadActivity")).ToBeVisibleAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP ready to save");
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadActivity")).ToBeHiddenAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadHeading")).ToBeFocusedAsync();
        }
        else
        {
            await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" })).ToHaveCountAsync(0);
            await Assertions.Expect(browser.Page.Locator("#downloadLogin"))
                .ToHaveAttributeAsync("href", "/Identity/Login?returnUrl=%2FMapDownloads%3Fgame%3Dcod4%26map%3Dmp_test");
        }

        var screenshot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestResults", $"native-download-folder-{width}.png"));
        await browser.Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true });
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFileJourney_LoadsLiveFiles_AndUsesStandardBrowserDownloadWithoutSecondLogin(bool authenticated)
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var folder = new ContentEntry("redirect/cod4/usermaps/mp_test", "mp_test", true, 3, DateTimeOffset.UnixEpoch, "v1", null);
        var fake = new FakeRedirectApiClient()
            .WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder))
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(3));
        await using var browser = await BrowserFixture.CreateAsync(authenticated ? TestPrincipalProfiles.GameAdmin : null, services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.AddInitScriptAsync("window.showSaveFilePicker = undefined;");
        await browser.Page.RouteAsync("**/Maps/GetMapListAjax**", route => Json(route,
            new { draw = 1, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>() }));
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Body = "abc", ContentType = "application/octet-stream",
            Headers = new Dictionary<string, string> { ["Content-Disposition"] = "attachment; filename=\"map.ff\"" }
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Link, new() { Name = "Map and mod downloads", Exact = true }).ClickAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Browse files" }).ClickAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Status).First).ToContainTextAsync("1 file");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create selected ZIP" })).ToHaveCountAsync(0);
        var login = browser.Page.GetByRole(AriaRole.Link, new() { Name = "Sign in to Portal for ZIP downloads" });
        if (authenticated)
            await Assertions.Expect(login).ToHaveCountAsync(0);
        else
            await Assertions.Expect(login).ToHaveAttributeAsync("href", "/Identity/Login?returnUrl=%2FMapDownloads");
        var download = await browser.Page.RunAndWaitForDownloadAsync(() => browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync());
        Assert.Equal("map.ff", download.SuggestedFilename);
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Sent to your browser download manager");
        Assert.All(fake.PortalCalls, caller => Assert.Equal(authenticated ? "12345" : null, caller.MemberId));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task AuthenticatedJourney_BoundsSelectionAt15_AutomaticallyPollsZip_AndDismissesReadyTasks()
    {
        var folders = Enumerable.Range(0, 16).Select(index => new ContentEntry(
            $"redirect/cod5/usermaps/mp_{index}", $"mp_{index}", true, 1024, DateTimeOffset.UnixEpoch, "v1", null)).ToArray();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, services =>
            services.AddFakeRedirectApiClient(fake => fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue(folders))));
        var polls = 0;
        await browser.Page.RouteAsync("**/MapDownloads/CreateArchive", async route =>
        {
            var request = JsonSerializer.Deserialize<CreateArchive>(route.Request.PostData!, JsonSerializerOptions.Web)!;
            Assert.Equal(15, request.Paths.Length);
            await Json(route, RedirectDtoFactory.CreateArchive());
        });
        await browser.Page.RouteAsync("**/MapDownloads/ArchiveStatus?**", route =>
        {
            polls++;
            return Json(route, RedirectDtoFactory.CreateArchive(status: polls == 1 ? "running" : "complete"));
        });
        await browser.Page.RouteAsync("**/MapDownloads/Collection?**", route => Json(route, new CataloguePage(
            route.Request.Url.Contains("page=2", StringComparison.Ordinal) ? folders[15..] : folders[..15],
            1, 15, 16, "collection-v1")));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod5").AbsoluteUri);
        var choices = browser.Page.Locator("[data-select-path]");
        await Assertions.Expect(choices).ToHaveCountAsync(15);
        for (var index = 0; index < 15; index++)
            await choices.Nth(index).ClickAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Assertions.Expect(choices).ToHaveCountAsync(1);
        await choices.First.ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadSelection")).ToHaveTextAsync("15 of 15 folders selected");
        await Assertions.Expect(choices.First).Not.ToBeCheckedAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Previous", Exact = true }).ClickAsync();
        await Assertions.Expect(choices).ToHaveCountAsync(15);
        await Assertions.Expect(choices.First).ToBeCheckedAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Select all", Exact = true })).ToHaveCountAsync(0);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create selected ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP queued");
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP running", new() { Timeout = 10000 });
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true })).ToBeVisibleAsync(new() { Timeout = 10000 });
        Assert.Equal(2, polls);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Check status" })).ToHaveCountAsync(0);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".native-download-task")).ToHaveCountAsync(0);
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task OtherMembersZip_IsNotDownloadable_AndPollingStops(int status)
    {
        var folder = new ContentEntry("redirect/cod4/usermaps/mp_test", "mp_test", true, 3, DateTimeOffset.UnixEpoch, "v1", null);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, services =>
            services.AddFakeRedirectApiClient(fake => fake
                .WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder))
                .WithData("CreateArchive", RedirectDtoFactory.CreateArchive())));
        var polls = 0;
        await browser.Page.RouteAsync("**/MapDownloads/ArchiveStatus?**", route =>
        {
            polls++;
            return route.FulfillAsync(new RouteFulfillOptions { Status = status, ContentType = "application/json", Body = /*lang=json,strict*/ "{\"message\":\"ZIP unavailable to this user\"}" });
        });
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP unavailable to this user", new() { Timeout = 10000 });
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP" })).ToHaveCountAsync(0);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
        Assert.Equal(1, polls);
    }

    [Fact]
    public async Task ResumableSave_StreamsDirectRangesToDisk_AndRenewsAfterReloadAtCommittedOffset()
    {
        const int chunk = 4 * 1024 * 1024;
        const int length = chunk + 3;
        var file = RedirectDtoFactory.CreateFile(FilePath, length);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(length));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        var ranges = new List<string>();
        var allowResume = false;
        await browser.Page.RouteAsync(DirectUrl, async route =>
        {
            var headers = await route.Request.AllHeadersAsync();
            var range = headers["range"];
            ranges.Add(range);
            Assert.Equal("\"test-version\"", headers["if-match"]);
            Assert.False(headers.ContainsKey("cookie"));
            Assert.False(headers.ContainsKey("authorization"));
            if (range != $"bytes=0-{chunk - 1}" && !allowResume)
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 403, Headers = Cors() });
                return;
            }

            var offset = range == $"bytes=0-{chunk - 1}" ? 0 : chunk;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 206, BodyBytes = offset == 0 ? new byte[chunk] : [1, 2, 3],
                Headers = new Dictionary<string, string>(Cors())
                {
                    ["ETag"] = "\"test-version\"",
                    ["Content-Range"] = $"bytes {offset}-{(offset == 0 ? chunk - 1 : length - 1)}/{length}",
                    ["Content-Type"] = "application/octet-stream"
                }
            });
        });
        var url = new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri;
        await browser.Page.GotoAsync(url);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("link expired", new() { Timeout = 15000 });
        Assert.Equal(chunk, await DiskSize(browser.Page));
        var stored = await browser.Page.EvaluateAsync<string>("""
            () => new Promise(resolve => {
                const open = indexedDB.open('portal-map-downloads', 1);
                open.onsuccess = () => {
                    const request = open.result.transaction('tasks').objectStore('tasks').getAll();
                    request.onsuccess = () => resolve(JSON.stringify(request.result));
                };
            })
            """);
        Assert.DoesNotContain(DirectUrl, stored, StringComparison.Ordinal);
        Assert.Contains("\"offset\":4194304", stored, StringComparison.Ordinal);
        Assert.Contains("\"owner\":\"\"", stored, StringComparison.Ordinal);
        allowResume = true;
        await browser.Page.ReloadAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Saved to disk", new() { Timeout = 15000 });
        Assert.Equal(length, await DiskSize(browser.Page));
        Assert.Equal(new[] { $"bytes=0-{chunk - 1}", $"bytes={chunk}-{length - 1}", $"bytes={chunk}-{length - 1}" }, ranges);
        Assert.Equal(2, fake.Calls.Count(call => call.StartsWith("CreateDownloadGrant:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("failed", "ZIP failed")]
    [InlineData("expired", "ZIP expired")]
    public async Task TerminalZipStates_AreVisibleWithoutStaleSaveActions(string state, string message)
    {
        var folder = new ContentEntry("redirect/cod4/usermaps/mp_test", "mp_test", true, 3, DateTimeOffset.UnixEpoch, "v1", null);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, services =>
            services.AddFakeRedirectApiClient(fake => fake
                .WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder))
                .WithData("CreateArchive", RedirectDtoFactory.CreateArchive(status: state))));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync(message);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP" })).ToHaveCountAsync(0);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator(".native-download-task")).ToHaveCountAsync(0);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Cod2PlainFileMaps_AndModFolders_UseNativeCollectionAndBrowse()
    {
        var plainMap = RedirectDtoFactory.CreateFile("redirect/cod2/main/mp_custom.iwd", 3);
        var modFile = RedirectDtoFactory.CreateFile("redirect/cod2/extreme/mod.iwd", 4);
        var folder = new ContentEntry("redirect/cod2/extreme", "extreme", true, 4, DateTimeOffset.UnixEpoch, "v1", null);
        var fake = new FakeRedirectApiClient()
            .WithData("Collection", RedirectDtoFactory.CreateCatalogue(plainMap))
            .WithData("Browse", RedirectDtoFactory.CreateCatalogue(modFile));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod2&map=mp_custom").AbsoluteUri);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" })).ToHaveCountAsync(0);
        Assert.Contains(fake.Calls, call => call == "Collection:cod2:maps");
        fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder));
        await browser.Page.Locator("#downloadQuery").FillAsync("");
        await browser.Page.Locator("#downloadKind").SelectOptionAsync("mods");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" })).ToBeVisibleAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Browse files" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadEntries")).ToContainTextAsync("mod.iwd");
        Assert.Contains(fake.Calls, call => call == "Collection:cod2:mods");
        Assert.Contains(fake.Calls, call => call == "Browse:redirect/cod2/extreme");
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("Manifest:", StringComparison.Ordinal));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task PersistedTransfer_FromAnotherOwner_IsPurgedWithoutRequestingTheirArchive()
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var fake = new FakeRedirectApiClient().WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await browser.Page.EvaluateAsync("""
            () => new Promise(resolve => {
                const open = indexedDB.open('portal-map-downloads', 1);
                open.onsuccess = () => {
                    const transaction = open.result.transaction('tasks', 'readwrite');
                    transaction.objectStore('tasks').put({
                        id:'private-task', owner:'12345', name:'Private ZIP', job:'private-archive',
                        expires:new Date(Date.now() + 3600000).toISOString()
                    });
                    transaction.oncomplete = resolve;
                };
            })
            """);
        await browser.Page.ReloadAsync();
        await browser.Page.WaitForFunctionAsync("""
            () => new Promise(resolve => {
                const open = indexedDB.open('portal-map-downloads', 1);
                open.onsuccess = () => {
                    const count = open.result.transaction('tasks').objectStore('tasks').count();
                    count.onsuccess = () => resolve(count.result === 0);
                };
            })
            """);
        await Assertions.Expect(browser.Page.Locator(".native-download-task")).ToHaveCountAsync(0);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("ArchiveStatus:", StringComparison.Ordinal));
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedZip_IsNotOverwrittenByItsOldExpiryTimer(bool disk)
    {
        var fake = ArchiveFake(3, "complete");
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.Clock.InstallAsync();
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = disk ? 206 : 200, Body = "abc", Headers = new Dictionary<string, string>(Cors())
            {
                ["ETag"] = "\"test-version\"", ["Content-Range"] = "bytes 0-2/3",
                ["Content-Disposition"] = "attachment; filename=\"map.ff\""
            }
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        if (!disk)
            await browser.Page.EvaluateAsync("window.showSaveFilePicker = undefined");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        var save = browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true });
        if (disk)
            await save.ClickAsync();
        else
            await browser.Page.RunAndWaitForDownloadAsync(() => save.ClickAsync());
        var expected = disk ? "Saved to disk" : "Sent to your browser download manager";
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync(expected);
        await browser.Page.Clock.FastForwardAsync(120000);
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync(expected);
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).Not.ToContainTextAsync("expired");
    }

    [Fact]
    public async Task DismissDuringPendingFileRead_DoesNotStartAnUntrackedTransfer()
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(3));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await browser.Page.EvaluateAsync("""
            () => {
                const getFile = FileSystemFileHandle.prototype.getFile;
                FileSystemFileHandle.prototype.getFile = function(...args) {
                    return new Promise(resolve => {
                        window.finishPendingFile = () => getFile.apply(this, args).then(resolve);
                    });
                };
                const fetch = window.fetch;
                window.directRequests = 0;
                window.fetch = (...args) => {
                    if (String(args[0]).startsWith('https://maps.test.invalid/')) window.directRequests++;
                    return fetch(...args);
                };
            }
            """);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync();
        await browser.Page.WaitForFunctionAsync("typeof window.finishPendingFile === 'function'");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
        await browser.Page.EvaluateAsync("async () => await window.finishPendingFile()");
        Assert.Equal(0, await browser.Page.EvaluateAsync<int>("window.directRequests"));
        await Assertions.Expect(browser.Page.Locator(".native-download-task")).ToHaveCountAsync(0);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task AdaptiveCheckpoints_GroupRangesWithoutReacquiringGrantsOrCopyingEachPrefix()
    {
        const int chunk = 4 * 1024 * 1024;
        const int length = 4 * chunk;
        var file = RedirectDtoFactory.CreateFile(FilePath, length);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(length));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        var ranges = 0;
        await browser.Page.RouteAsync(DirectUrl, route =>
        {
            ranges++;
            var range = route.Request.Headers["range"]["bytes=".Length..].Split('-');
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 206, BodyBytes = new byte[chunk], Headers = new Dictionary<string, string>(Cors())
                {
                    ["ETag"] = "\"test-version\"", ["Content-Range"] = $"bytes {range[0]}-{range[1]}/{length}"
                }
            });
        });
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await browser.Page.EvaluateAsync("""
            () => {
                const createWritable = FileSystemFileHandle.prototype.createWritable;
                window.writerCount = 0;
                FileSystemFileHandle.prototype.createWritable = function(...args) {
                    window.writerCount++;
                    return createWritable.apply(this, args);
                };
            }
            """);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Saved to disk", new() { Timeout = 20000 });
        Assert.Equal(length, await DiskSize(browser.Page));
        Assert.Equal(4, ranges);
        Assert.Equal(3, await browser.Page.EvaluateAsync<int>("window.writerCount"));
        Assert.Single(fake.Calls, call => call.StartsWith("CreateDownloadGrant:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestoredPausedZip_RearmsExpiryWithoutLosingItsHandleOrOffset()
    {
        const int chunk = 4 * 1024 * 1024;
        var fake = ArchiveFake(chunk + 3, "complete");
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.Clock.InstallAsync();
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = route.Request.Headers["range"].StartsWith("bytes=0-", StringComparison.Ordinal) ? 206 : 403,
            BodyBytes = new byte[chunk], Headers = new Dictionary<string, string>(Cors())
            {
                ["ETag"] = "\"test-version\"", ["Content-Range"] = $"bytes 0-{chunk - 1}/{chunk + 3}"
            }
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("link expired");
        Assert.Equal(chunk, await DiskSize(browser.Page));
        await browser.Page.ReloadAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true })).ToBeVisibleAsync();
        await browser.Page.Clock.FastForwardAsync(120000);
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP expired");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true })).ToHaveCountAsync(0);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("ArchiveStatus:", StringComparison.Ordinal));
        Assert.Equal(chunk, await DiskSize(browser.Page));
    }

    [Fact]
    public async Task BackForwardCacheReturn_RestartsZipPollingAndReadyExpiry()
    {
        var fake = ArchiveFake(3, "queued").WithData("ArchiveStatus",
            RedirectDtoFactory.CreateArchive(status: "complete") with { Bytes = 3, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1) });
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.Clock.InstallAsync();
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP queued");
        await browser.Page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pagehide', {persisted:true}))");
        await browser.Page.Clock.FastForwardAsync(5000);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("ArchiveStatus:", StringComparison.Ordinal));
        await browser.Page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pageshow', {persisted:true}))");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true })).ToBeVisibleAsync();
        Assert.Single(fake.Calls, call => call.StartsWith("ArchiveStatus:", StringComparison.Ordinal));
        await browser.Page.Clock.FastForwardAsync(120000);
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP expired");
    }

    [Fact]
    public async Task ConcurrentTabResume_IsRejected_AndCompletedCheckpointCannotBeReplayed()
    {
        const int chunk = 4 * 1024 * 1024;
        const int length = chunk + 3;
        var file = RedirectDtoFactory.CreateFile(FilePath, length);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(length));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        var resuming = false;
        var pendingRange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await browser.Page.Context.RouteAsync(DirectUrl, async route =>
        {
            var first = route.Request.Headers["range"].StartsWith("bytes=0-", StringComparison.Ordinal);
            if (!first && !resuming)
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 403, Headers = Cors() });
                return;
            }

            if (!first)
            {
                pendingRange.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 206, BodyBytes = first ? [.. Enumerable.Repeat((byte)7, chunk)] : [1, 2, 3],
                Headers = new Dictionary<string, string>(Cors())
                {
                    ["ETag"] = "\"test-version\"",
                    ["Content-Range"] = first ? $"bytes 0-{chunk - 1}/{length}" : $"bytes {chunk}-{length - 1}/{length}"
                }
            });
        });
        var url = new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri;
        await browser.Page.GotoAsync(url);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("link expired");
        var other = await browser.Page.Context.NewPageAsync();
        await using var otherPicker = await InstallDiskPicker(other);
        await other.GotoAsync(url);
        await Assertions.Expect(other.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true })).ToBeVisibleAsync();
        resuming = true;
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
        await pendingRange.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await other.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
            await Assertions.Expect(other.Locator("#downloadTasks")).ToContainTextAsync("Another Portal save is running");
            Assert.Equal(2, fake.Calls.Count(call => call.StartsWith("CreateDownloadGrant:", StringComparison.Ordinal)));
        }
        finally
        {
            release.TrySetResult();
        }

        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Saved to disk");
        await other.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
        await Assertions.Expect(other.Locator("#downloadTasks")).ToContainTextAsync("completed or was dismissed in another tab");
        Assert.Equal(2, fake.Calls.Count(call => call.StartsWith("CreateDownloadGrant:", StringComparison.Ordinal)));
        Assert.Equal(length, await DiskSize(browser.Page));
        var content = await browser.Page.EvaluateAsync<int[]>("""
            async () => {
                const file = await (await window.showSaveFilePicker()).getFile();
                return [...new Uint8Array(await file.slice(0, 1).arrayBuffer()), ...new Uint8Array(await file.slice(-3).arrayBuffer())];
            }
            """);
        Assert.Equal([7, 1, 2, 3], content);
    }

    [Fact]
    public async Task FolderZip_SubmissionPreservesOtherAndNewlySelectedFolders()
    {
        var folders = Enumerable.Range(0, 3).Select(index => new ContentEntry(
            $"redirect/cod4/usermaps/mp_{index}", $"mp_{index}", true, 3, DateTimeOffset.UnixEpoch, "v1", null)).ToArray();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, services =>
            services.AddFakeRedirectApiClient(fake => fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue(folders))));
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await browser.Page.RouteAsync("**/MapDownloads/CreateArchive", async route =>
        {
            var request = JsonSerializer.Deserialize<CreateArchive>(route.Request.PostData!, JsonSerializerOptions.Web)!;
            Assert.Equal([folders[1].Path], request.Paths);
            pending.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Json(route, RedirectDtoFactory.CreateArchive());
        });
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        var choices = browser.Page.Locator("[data-select-path]");
        await choices.Nth(0).CheckAsync();
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).Nth(1).ClickAsync();
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await choices.Nth(2).CheckAsync();
        }
        finally
        {
            release.TrySetResult();
        }

        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP queued");
        await Assertions.Expect(browser.Page.Locator("#downloadSelection")).ToHaveTextAsync("2 of 15 folders selected");
        await Assertions.Expect(choices.Nth(0)).ToBeCheckedAsync();
        await Assertions.Expect(choices.Nth(2)).ToBeCheckedAsync();
    }

    [Fact]
    public async Task FullyCommittedRestoredFile_FinalizesWithoutRequestingAReversedRange()
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(3));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await browser.Page.EvaluateAsync("""
            async () => {
                const handle = await window.showSaveFilePicker();
                const writer = await handle.createWritable();
                await writer.write('abc');
                await writer.close();
                const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode('abc')))]
                    .map(value => value.toString(16).padStart(2, '0')).join('');
                await new Promise(resolve => {
                    const open = indexedDB.open('portal-map-downloads', 1);
                    open.onsuccess = () => {
                        const transaction = open.result.transaction('tasks', 'readwrite');
                        transaction.objectStore('tasks').put({
                            id:'final-checkpoint', owner:'', handle, name:'map.ff',
                            resource:{path:'redirect/cod4/usermaps/mp_test/map.ff', version:'test-version'},
                            offset:3, total:3, version:'test-version', integrity:[hash]
                        });
                        transaction.oncomplete = resolve;
                    };
                });
            }
            """);
        await browser.Page.ReloadAsync();
        await browser.Page.EvaluateAsync("""
            () => {
                const fetch = window.fetch;
                window.directRequests = 0;
                window.fetch = (...args) => {
                    if (String(args[0]).startsWith('https://maps.test.invalid/')) window.directRequests++;
                    return fetch(...args);
                };
            }
            """);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Saved to disk");
        Assert.Equal(0, await browser.Page.EvaluateAsync<int>("window.directRequests"));
        Assert.Equal(3, await DiskSize(browser.Page));
        Assert.Single(fake.Calls, call => call.StartsWith("CreateDownloadGrant:", StringComparison.Ordinal));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task DelayedZipStatus_InAnotherTab_PreservesTheCommittedFileCheckpoint()
    {
        const int chunk = 4 * 1024 * 1024;
        var fake = ArchiveFake(chunk + 3, "queued");
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.Clock.InstallAsync(new() { Time = "2026-01-01T00:00:00Z" });
        await browser.Page.Clock.PauseAtAsync(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP queued");
        var other = await browser.Page.Context.NewPageAsync();
        await using var otherPicker = await InstallDiskPicker(other);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = RedirectDtoFactory.CreateArchive(status: "complete") with
        {
            Bytes = chunk + 3,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        await browser.Page.Context.RouteAsync("**/MapDownloads/ArchiveStatus?**", async route =>
        {
            if (route.Request.Frame.Page == other)
            {
                pending.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            await Json(route, ready);
        });
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = route.Request.Headers["range"].StartsWith("bytes=0-", StringComparison.Ordinal) ? 206 : 403,
            BodyBytes = new byte[chunk],
            Headers = new Dictionary<string, string>(Cors())
            {
                ["ETag"] = "\"test-version\"", ["Content-Range"] = $"bytes 0-{chunk - 1}/{chunk + 3}"
            }
        }));
        await other.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await browser.Page.Clock.FastForwardAsync(2500);
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true }).ClickAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("link expired");
        }
        finally
        {
            release.TrySetResult();
        }

        await Assertions.Expect(other.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true })).ToBeVisibleAsync();
        await browser.Page.ReloadAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true })).ToBeVisibleAsync();
        Assert.Equal(chunk, await DiskSize(browser.Page));
        var record = await browser.Page.EvaluateAsync<string>("""
            () => new Promise(resolve => {
                const open = indexedDB.open('portal-map-downloads', 1);
                open.onsuccess = () => {
                    const request = open.result.transaction('tasks').objectStore('tasks').getAll();
                    request.onsuccess = () => resolve(JSON.stringify(request.result.map(record => ({
                        offset:record.offset, version:record.version, hashes:record.integrity.length, handle:!!record.handle
                    }))));
                };
            })
            """);
        Assert.Contains("\"offset\":4194304", record, StringComparison.Ordinal);
        Assert.Contains("\"hashes\":1", record, StringComparison.Ordinal);
        Assert.Contains("\"handle\":true", record, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusedDestination_NeverReceivesAnotherFilesResumedSuffix(bool externalOverwrite)
    {
        const int chunk = 4 * 1024 * 1024;
        const int length = chunk + 3;
        const int replacementLength = chunk + 5;
        var first = RedirectDtoFactory.CreateFile(FilePath, length);
        var second = RedirectDtoFactory.CreateFile("redirect/cod4/usermaps/mp_test/other.ff", replacementLength);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [first, second]))
            .WithData("CreateDownloadGrant", Grant(length));
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        var replacing = false;
        var requests = 0;
        await browser.Page.Context.RouteAsync(DirectUrl, route =>
        {
            requests++;
            var range = route.Request.Headers["range"]["bytes=".Length..].Split('-');
            if (!replacing && range[0] != "0")
                return route.FulfillAsync(new RouteFulfillOptions { Status = 403, Headers = Cors() });
            var count = int.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture)
                - int.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture) + 1;
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 206, BodyBytes = [.. Enumerable.Repeat(replacing ? (byte)9 : (byte)7, count)],
                Headers = new Dictionary<string, string>(Cors())
                {
                    ["ETag"] = "\"test-version\"",
                    ["Content-Range"] = $"bytes {range[0]}-{range[1]}/{(replacing ? replacementLength : length)}"
                }
            });
        });
        var url = new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri;
        await browser.Page.GotoAsync(url);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).Nth(0).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("link expired");
        var other = await browser.Page.Context.NewPageAsync();
        await using var otherPicker = await InstallDiskPicker(other);
        await other.GotoAsync(url);
        await Assertions.Expect(other.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true })).ToBeVisibleAsync();
        if (externalOverwrite)
        {
            await browser.Page.EvaluateAsync("""
                async size => {
                    const writer = await (await window.showSaveFilePicker()).createWritable();
                    await writer.write(new Uint8Array(size).fill(9));
                    await writer.close();
                }
                """, replacementLength);
        }
        else
        {
            replacing = true;
            fake.WithData("CreateDownloadGrant", Grant(replacementLength));
            await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).Nth(1).ClickAsync();
            await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Saved to disk");
        }

        var beforeResume = requests;
        await other.GetByRole(AriaRole.Button, new() { Name = "Resume save", Exact = true }).ClickAsync();
        await Assertions.Expect(other.Locator("#downloadTasks")).ToContainTextAsync(
            externalOverwrite ? "saved file was changed or replaced" : "destination was reused");
        Assert.Equal(beforeResume, requests);
        Assert.Equal(replacementLength, await DiskSize(browser.Page));
        var content = await browser.Page.EvaluateAsync<int[]>("""
            async () => {
                const file = await (await window.showSaveFilePicker()).getFile();
                return [...new Uint8Array(await file.slice(0, 1).arrayBuffer()), ...new Uint8Array(await file.slice(-1).arrayBuffer())];
            }
            """);
        Assert.Equal([9, 9], content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedZipStatus_CannotResurrectCompletedOrDismissedRecords(bool dismiss)
    {
        var fake = ArchiveFake(3, "queued");
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin,
            services => services.AddSingleton<IRedirectApiClient>(fake));
        await browser.Page.Clock.InstallAsync(new() { Time = "2026-01-01T00:00:00Z" });
        await browser.Page.Clock.PauseAtAsync(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await browser.Page.EvaluateAsync("window.showSaveFilePicker = undefined");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Create folder ZIP" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP queued");
        var other = await browser.Page.Context.NewPageAsync();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = RedirectDtoFactory.CreateArchive(status: "complete") with
        {
            Bytes = 3,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        await browser.Page.Context.RouteAsync("**/MapDownloads/ArchiveStatus?**", async route =>
        {
            if (route.Request.Frame.Page == other)
            {
                pending.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            await Json(route, ready);
        });
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Body = "abc",
            Headers = new Dictionary<string, string> { ["Content-Disposition"] = "attachment; filename=\"map.ff\"" }
        }));
        await other.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads").AbsoluteUri);
        await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await browser.Page.Clock.FastForwardAsync(2500);
            var save = browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true });
            await Assertions.Expect(save).ToBeVisibleAsync();
            if (dismiss)
                await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss download task" }).ClickAsync();
            else
            {
                await browser.Page.RunAndWaitForDownloadAsync(() => save.ClickAsync());
                await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("Sent to your browser download manager");
            }
        }
        finally
        {
            release.TrySetResult();
        }

        await Assertions.Expect(other.Locator("#downloadTasks")).ToContainTextAsync("completed or was dismissed in another tab");
        await Assertions.Expect(other.GetByRole(AriaRole.Button, new() { Name = "Save ZIP", Exact = true })).ToHaveCountAsync(0);
        await other.ReloadAsync();
        var count = await other.EvaluateAsync<int>("""
            () => new Promise(resolve => {
                const open = indexedDB.open('portal-map-downloads', 1);
                open.onsuccess = () => {
                    const request = open.result.transaction('tasks').objectStore('tasks').count();
                    request.onsuccess = () => resolve(request.result);
                };
            })
            """);
        Assert.Equal(0, count);
        await Assertions.Expect(other.Locator(".native-download-task")).ToHaveCountAsync(0);
    }

    private static FakeRedirectApiClient ArchiveFake(long bytes, string state)
    {
        var folder = new ContentEntry("redirect/cod4/usermaps/mp_test", "mp_test", true, bytes, DateTimeOffset.UnixEpoch, "v1", null);
        return new FakeRedirectApiClient()
            .WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder))
            .WithData("CreateArchive", RedirectDtoFactory.CreateArchive(status: state) with
            {
                Bytes = bytes,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1)
            })
            .WithData("CreateDownloadGrant", Grant(bytes));
    }

    private static DownloadGrant Grant(long bytes)
    {
        return new DownloadGrant(DirectUrl, "map.ff", bytes, "test-version", DateTimeOffset.UtcNow.AddMinutes(15));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("full-response")]
    [InlineData("wrong-range")]
    [InlineData("wrong-etag")]
    [InlineData("short-body")]
    [InlineData("long-body")]
    public async Task UnsafeTransferMetadata_NeverWritesOrAppends(string failure)
    {
        var file = RedirectDtoFactory.CreateFile(FilePath, 3);
        var fake = new FakeRedirectApiClient()
            .WithData("Manifest", RedirectDtoFactory.CreateManifest(files: [file]))
            .WithData("CreateDownloadGrant", Grant(3) with { Version = failure == "changed" ? "new-version" : file.Version });
        await using var browser = await BrowserFixture.CreateAsync(configureServices: services =>
            services.AddSingleton<IRedirectApiClient>(fake));
        await using var picker = await InstallDiskPicker(browser.Page);
        await browser.Page.RouteAsync(DirectUrl, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = failure == "full-response" ? 200 : 206,
            Body = failure == "short-body" ? "ab" : failure == "long-body" ? "abcd" : "abc",
            Headers = new Dictionary<string, string>(Cors())
            {
                ["Content-Range"] = failure == "wrong-range" ? "bytes 1-2/3" : "bytes 0-2/3",
                ["ETag"] = failure == "wrong-etag" ? "\"new-version\"" : "\"test-version\""
            }
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/MapDownloads?game=cod4&map=mp_test").AbsoluteUri);
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Save file", Exact = true }).ClickAsync();
        var message = failure switch
        {
            "changed" => "Content changed",
            "short-body" => "Download interrupted",
            "long-body" => "Invalid download range length",
            _ => "could not safely resume"
        };
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync(message);
        Assert.Equal(0, await DiskSize(browser.Page));
    }

    private static Task Json(IRoute route, object value)
    {
        return route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = JsonSerializer.Serialize(value, JsonSerializerOptions.Web) });
    }

    private static Dictionary<string, string> Cors()
    {
        return new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Expose-Headers"] = "ETag,Content-Range,Content-Length",
        };
    }

    private static Task<IAsyncDisposable> InstallDiskPicker(IPage page)
    {
        return page.AddInitScriptAsync("""
            FileSystemFileHandle.prototype.requestPermission = async () => 'granted';
            window.showSaveFilePicker = async () => (await navigator.storage.getDirectory()).getFileHandle('native-test.ff', {create:true});
            // Headless Chromium on Windows crashes deserializing an OPFS handle from IndexedDB.
            // Keep real IndexedDB metadata and real disk IO, replacing only the test picker handle.
            const put = IDBObjectStore.prototype.put;
            IDBObjectStore.prototype.put = function(value, ...args) {
                return put.call(this, this.name === 'tasks' && value.handle ? {...value, handle:null, testDiskHandle:true} : value, ...args);
            };
            const getAll = IDBObjectStore.prototype.getAll;
            IDBObjectStore.prototype.getAll = function(...args) {
                const request = getAll.call(this, ...args);
                if (this.name === 'tasks') Object.defineProperty(request, 'onsuccess', {
                    set(handler) {
                        request.addEventListener('success', async event => {
                            for (const task of request.result) if (task.testDiskHandle)
                                task.handle = await window.showSaveFilePicker();
                            handler(event);
                        });
                    }
                });
                return request;
            };
            """);
    }

    private static Task<int> DiskSize(IPage page)
    {
        return page.EvaluateAsync<int>("async () => (await (await (await navigator.storage.getDirectory()).getFileHandle('native-test.ff')).getFile()).size");
    }
}
