using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Redirect.Api.Abstractions.V1;
using XtremeIdiots.Redirect.Api.Client.Testing;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed partial class NativeMapDownloadsHttpTests
{
    private readonly static string[] untrustedPaths = ["redirect/cod4/mods/untrusted"];

    [Fact]
    public async Task ServerDetails_LinksToServerDerivedZip_AndUsesSharedObservedMapPopularity()
    {
        await using var host = await CreateHost();
        var id = Guid.NewGuid();
        SetRotation(host, new PublicServerRotationDto
        {
            GameServerId = id, GameType = GameType.CallOfDuty4x, IsObservedHistory = true,
            Maps = [new() { MapName = "mp_history", Likes = 2, Dislikes = 1 }]
        });
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                GameServerId = id,
                GameType = GameType.CallOfDuty4x,
                Title = "Synthetic server",
                Hostname = "192.0.2.10",
                QueryPort = 28960,
                ServerListEnabled = true
            }));
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GameServers.V1.GetGameServer(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        repository.Setup(r => r.LiveStatus.V1.GetGameServerLiveStatus(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerLiveStatusDto>(HttpStatusCode.NotFound));
        using var page = await host.Client.GetAsync($"/Servers/Details/{id}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"/MapDownloads?rotation={id}", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var response = await host.Client.GetAsync($"/Servers/GetPublicMapRotation/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var projection = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(projection.RootElement.GetProperty("isObservedHistory").GetBoolean());
        var map = Assert.Single(projection.RootElement.GetProperty("maps").EnumerateArray());
        Assert.Equal("mp_history", map.GetProperty("mapName").GetString());
        Assert.Equal(3, map.GetProperty("votes").GetInt32());
    }

    [Fact]
    public async Task PublicInsights_NormalizesMapFamily_RejectsHiddenOrMismatchedServers_AndValidatesFilters()
    {
        await using var host = await CreateHost();
        var id = Guid.NewGuid();
        SetRotation(host, new PublicServerRotationDto { GameServerId = id, GameType = GameType.CallOfDuty4x });
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.MapAnalytics.V1.GetInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            GameType.CallOfDuty4, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapInsightsDto>(HttpStatusCode.OK, new ApiResponse<MapInsightsDto>(new MapInsightsDto { TotalLikes = 2 })));
        using var valid = await host.Client.GetAsync($"/Maps/Insights?gameType=CallOfDuty4x&gameServerId={id}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(2, (await valid.Content.ReadFromJsonAsync<MapInsightsDto>())!.TotalVotes);
        repository.Verify(r => r.MapAnalytics.V1.GetInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            GameType.CallOfDuty4, id, It.IsAny<CancellationToken>()), Times.Once);
        using var hidden = await host.Client.GetAsync($"/Maps/Insights?gameServerId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var mismatch = await host.Client.GetAsync($"/Maps/Insights?gameType=CallOfDuty5&gameServerId={id}");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using var invalid = await host.Client.GetAsync("/Maps/Insights?days=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task RotationZip_IsExplicitAuthenticatedAndAntiforgeryProtected_AndDerivesOnlyAvailableCustomMaps()
    {
        await using var host = await CreateHost();
        var serverId = Guid.NewGuid();
        SetRotation(host, new PublicServerRotationDto
        {
            GameServerId = serverId, GameType = GameType.CallOfDuty4x,
            Maps = [new() { MapName = "mp_crash" }, new() { MapName = "mp_custom" },
                new() { MapName = "MP_CUSTOM" }, new() { MapName = "mp_missing" }]
        });
        var fake = host.Services.GetRequiredService<FakeRedirectApiClient>();
        var folder = new ContentEntry("redirect/cod4/usermaps/mp_custom", "mp_custom", true, 100,
            DateTimeOffset.UnixEpoch, "v1", null);
        fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue(folder,
            folder with { Path = "redirect/cod4/mods/mp_custom" },
            folder with { Name = "mp_crash", Path = "redirect/cod4/usermaps/mp_crash" }))
            .WithData("CreateArchive", RedirectDtoFactory.CreateArchive(status: "complete"));
        using var page = await host.Client.GetAsync($"/MapDownloads?rotation={serverId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Empty(fake.Calls);
        using var anonymous = await host.Client.PostAsJsonAsync("/MapDownloads/CreateRotationArchive", new { gameServerId = serverId });
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        using var noToken = new HttpRequestMessage(HttpMethod.Post, "/MapDownloads/CreateRotationArchive")
        {
            Content = JsonContent.Create(new { gameServerId = serverId })
        };
        noToken.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var rejected = await host.Client.SendAsync(noToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(fake.Calls);
        using var request = await Post(host, "/MapDownloads/CreateRotationArchive",
            new { gameServerId = serverId, paths = untrustedPaths }, true);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<ArchiveJob>();
        Assert.Null(job!.DownloadUrl);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Contains("Collection:cod4:maps", fake.Calls);
        Assert.Contains("CreateArchive:redirect/cod4/usermaps/mp_custom", fake.Calls);
        Assert.All(fake.PortalCalls, caller => Assert.Equal("12345", caller.MemberId));
    }

    [Theory]
    [InlineData(GameType.CallOfDuty2, 1, 400)]
    [InlineData(GameType.CallOfDuty4, 0, 422)]
    [InlineData(GameType.CallOfDuty4, 101, 413)]
    public async Task RotationZip_RejectsUnsupportedEmptyAndOversizedRotationsWithoutCreatingPartialJobs(GameType game, int count, int status)
    {
        await using var host = await CreateHost();
        var id = Guid.NewGuid();
        SetRotation(host, new PublicServerRotationDto
        {
            GameServerId = id, GameType = game,
            Maps = [.. Enumerable.Range(0, count).Select(i => new PublicRotationMapDto { MapName = $"mp_custom_{i}" })]
        });
        var fake = host.Services.GetRequiredService<FakeRedirectApiClient>();
        fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue([.. Enumerable.Range(0, count).Select(i =>
            new ContentEntry($"redirect/cod4/usermaps/mp_custom_{i}", $"mp_custom_{i}", true, 1,
                DateTimeOffset.UnixEpoch, "v1", null))]));
        using var request = await Post(host, "/MapDownloads/CreateRotationArchive", new { gameServerId = id }, true);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("CreateArchive:", StringComparison.Ordinal));
    }

    private static void SetRotation(PortalWebTestHost host, PublicServerRotationDto server)
    {
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
                .Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                    new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto { Servers = [server] })));
    }

    [Fact]
    public async Task AnonymousFileGrant_RequiresAntiforgery_AndUsesServerFingerprint()
    {
        await using var host = await CreateHost();
        var fake = host.Services.GetRequiredService<FakeRedirectApiClient>();
        var file = RedirectDtoFactory.CreateFile();
        fake.WithData("CreateDownloadGrant", new DownloadGrant("https://maps.test.invalid/v1/downloads/" + new string('a', 64),
            file.Name, file.Bytes, file.Version, DateTimeOffset.UtcNow.AddMinutes(15)));
        using var rejected = await host.Client.PostAsJsonAsync("/MapDownloads/Grant", new { file.Path, file.Version, memberId = "spoof", ipAddress = "203.0.113.99" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(fake.PortalCalls);

        using var request = await Post(host, "/MapDownloads/Grant", new { file.Path, file.Version, memberId = "spoof", ipAddress = "203.0.113.99" });
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var caller = Assert.Single(fake.PortalCalls);
        Assert.Null(caller.MemberId);
        Assert.Equal("192.0.2.15", caller.IpAddress);
        Assert.Equal("Synthetic native download test", caller.UserAgent);
        using var archive = await host.Client.GetAsync("/MapDownloads/ArchiveStatus?id=private");
        Assert.Equal(HttpStatusCode.Redirect, archive.StatusCode);
        Assert.Contains("/Identity/Login?ReturnUrl=", archive.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Single(fake.PortalCalls);
    }

    [Fact]
    public async Task LoggedInArchives_EnforceAntiforgeryAndFolderBound_AndPreserveOwnershipFailures()
    {
        await using var host = await CreateHost();
        var fake = host.Services.GetRequiredService<FakeRedirectApiClient>();
        fake.WithData("CreateArchive", RedirectDtoFactory.CreateArchive());
        using var noToken = new HttpRequestMessage(HttpMethod.Post, "/MapDownloads/CreateArchive")
        {
            Content = JsonContent.Create(new CreateArchive(["redirect/cod4/usermaps/mp_one"]))
        };
        noToken.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var rejected = await host.Client.SendAsync(noToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var tooMany = await Post(host, "/MapDownloads/CreateArchive", new CreateArchive(
            [.. Enumerable.Range(0, 101).Select(index => $"redirect/cod4/usermaps/mp_{index}")]), true);
        using var tooManyResponse = await host.Client.SendAsync(tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
        Assert.Empty(fake.PortalCalls);

        using var valid = await Post(host, "/MapDownloads/CreateArchive", new CreateArchive(["redirect/cod4/usermaps/mp_one"]), true);
        using var validResponse = await host.Client.SendAsync(valid);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.Equal("12345", Assert.Single(fake.PortalCalls).MemberId);

        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            fake.Respond("ArchiveStatus", new ApiResult<ArchiveJob>(code, new ApiResponse<ArchiveJob>(new ApiError("OWNED", "private"))));
            using var status = new HttpRequestMessage(HttpMethod.Get, "/MapDownloads/ArchiveStatus?id=someone-elses-job");
            status.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
            using var response = await host.Client.SendAsync(status);
            Assert.Equal(code, response.StatusCode);
        }

        Assert.All(fake.PortalCalls, caller => Assert.Equal("12345", caller.MemberId));
    }

    [Fact]
    public async Task AuthenticatedPublicFiles_UsePortalContext_AndPreserveRateLimits()
    {
        await using var host = await CreateHost();
        var fake = host.Services.GetRequiredService<FakeRedirectApiClient>();
        var file = RedirectDtoFactory.CreateFile();
        fake.WithData("Collection", RedirectDtoFactory.CreateCatalogue(file))
            .WithData("CreateDownloadGrant", new DownloadGrant("https://maps.test.invalid/v1/downloads/" + new string('a', 64),
                file.Name, file.Bytes, file.Version, DateTimeOffset.UtcNow.AddMinutes(15)));
        using var metadata = new HttpRequestMessage(HttpMethod.Get, "/MapDownloads/Collection?game=cod4");
        metadata.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var metadataResponse = await host.Client.SendAsync(metadata);
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        using var grant = await Post(host, "/MapDownloads/Grant", new { file.Path, file.Version }, true);
        using var grantResponse = await host.Client.SendAsync(grant);
        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        fake.Respond("CreateDownloadGrant", new ApiResult<DownloadGrant>(HttpStatusCode.TooManyRequests,
            new ApiResponse<DownloadGrant>(new ApiError("RATE", "private fingerprint"))));
        using var limited = await Post(host, "/MapDownloads/Grant", new { file.Path, file.Version }, true);
        using var limitedResponse = await host.Client.SendAsync(limited);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.DoesNotContain("private fingerprint", await limitedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(3, fake.PortalCalls.Count);
        Assert.All(fake.PortalCalls, caller => Assert.Equal("12345", caller.MemberId));
    }

    private static Task<PortalWebTestHost> CreateHost()
    {
        return PortalWebTestHost.CreateAsync(services => services.AddSingleton<IStartupFilter, ConnectionAddressFilter>());
    }

    private async static Task<HttpRequestMessage> Post(PortalWebTestHost host, string url, object body, bool authenticated = false)
    {
        using var get = new HttpRequestMessage(HttpMethod.Get, "/MapDownloads");
        if (authenticated)
            get.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var page = await host.Client.SendAsync(get);
        var html = await page.Content.ReadAsStringAsync();
        var token = TokenRegex().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        request.Headers.Add("Cookie", string.Join("; ", page.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        request.Headers.Add("User-Agent", "Synthetic native download test");
        if (authenticated)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        return request;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private sealed class ConnectionAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, following) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.15");
                    return following(context);
                });
                next(app);
            };
        }
    }
}
