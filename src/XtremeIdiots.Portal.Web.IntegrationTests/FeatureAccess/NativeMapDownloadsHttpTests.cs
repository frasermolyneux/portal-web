using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MX.Api.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Redirect.Api.Abstractions.V1;
using XtremeIdiots.Redirect.Api.Client.Testing;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed partial class NativeMapDownloadsHttpTests
{
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
            [.. Enumerable.Range(0, 16).Select(index => $"redirect/cod4/usermaps/mp_{index}")]), true);
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
