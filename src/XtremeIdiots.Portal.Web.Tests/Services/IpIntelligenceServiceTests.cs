using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Abstractions.Models.V1_1;
using MX.GeoLocation.Api.Client.V1;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class IpIntelligenceServiceTests
{
    [Fact]
    public async Task ConcurrentMissesAcrossScopesShareOneFullTypedResult()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var client = new Mock<IGeoLocationApiClient> { DefaultValue = DefaultValue.Mock };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ApiResult<IpIntelligenceDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence("2001:db8::1", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.TrySetResult();
                return release.Task;
            });
        var firstService = new IpIntelligenceService(client.Object, cache, coordinator, NullLogger<IpIntelligenceService>.Instance);
        var secondService = new IpIntelligenceService(client.Object, cache, coordinator, NullLogger<IpIntelligenceService>.Instance);
        var first = firstService.GetAsync("2001:db8::1");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = secondService.GetAsync("2001:0db8:0:0:0:0:0:1");
        var intelligence = JsonConvert.DeserializeObject<IpIntelligenceDto>(
                                 /*lang=json,strict*/
                                 """{"countryCode":"GB","cityName":"London","timezone":"Europe/London","proxyCheckStatus":"Success","proxyCheck":{"riskScore":0,"isProxy":false,"isVpn":false,"asOrganization":"Example network"}}""");
        release.SetResult(new(HttpStatusCode.OK, new ApiResponse<IpIntelligenceDto>(intelligence)));
        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.Same(intelligence, results[0].Intelligence);
        Assert.Equal("available", results[0].Status);
        Assert.Equal(0, results[0].RiskScore);
        Assert.False(results[0].IsProxy);
        client.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence("2001:db8::1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedRiskSourceKeepsGeographyButNeverReportsSafeDefaults()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var client = new Mock<IGeoLocationApiClient> { DefaultValue = DefaultValue.Mock };
        var intelligence = JsonConvert.DeserializeObject<IpIntelligenceDto>(
                                 /*lang=json,strict*/
                                 """{"countryCode":"GB","proxyCheckStatus":2147483647,"proxyCheck":{"riskScore":0,"isProxy":false}}""");
        client.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence("203.0.113.10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<IpIntelligenceDto>(HttpStatusCode.OK, new ApiResponse<IpIntelligenceDto>(intelligence)));
        var service = new IpIntelligenceService(client.Object, cache, coordinator, NullLogger<IpIntelligenceService>.Instance);

        var result = await service.GetAsync("203.0.113.10");

        Assert.Equal("GB", result.Intelligence?.CountryCode);
        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.RiskScore);
        Assert.Null(result.IsProxy);
        Assert.Null(result.IsVpn);
    }

    [Fact]
    public async Task DependencyTimeoutIsBrieflyCachedButRequestCancellationIsNot()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var client = new Mock<IGeoLocationApiClient> { DefaultValue = DefaultValue.Mock };
        using var cancellation = new CancellationTokenSource();
        client.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence("203.0.113.10", It.IsAny<CancellationToken>()))
            .Returns(async (string _, CancellationToken token) =>
            {
                await cancellation.CancelAsync();
                return await Task.FromCanceled<ApiResult<IpIntelligenceDto>>(token);
            });
        var service = new IpIntelligenceService(client.Object, cache, coordinator, NullLogger<IpIntelligenceService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync("203.0.113.10", cancellation.Token));
        client.Setup(x => x.GeoLookup.V1_1.GetIpIntelligence("203.0.113.10", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Dependency timeout"));

        var result = await service.GetAsync("203.0.113.10");
        Assert.Same(result, await service.GetAsync("203.0.113.10"));
        Assert.Null(result.Intelligence);
        Assert.Equal("unavailable", result.Status);
        Assert.InRange(result.ExpiresAtUtc - DateTime.UtcNow, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        client.Verify(x => x.GeoLookup.V1_1.GetIpIntelligence("203.0.113.10", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
