using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using MX.GeoLocation.Abstractions.Models.V1_1;
using MX.GeoLocation.Api.Client.V1;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

/// <summary>
/// Shapes the repository and geo-location mocks so <c>/Players/Details/{id}</c> renders a single,
/// fully-populated player. The player DTO is supplied by <see cref="PlayerDtoBuilder"/> so each test
/// controls the data shape it wants to exercise (risk score, ban state, counts, related players).
/// The geo mock returns a deterministic <see cref="IpIntelligenceDto"/> for every IP so the IP
/// Intelligence panel, risk gauge and proxy/VPN badges render, unless <c>includeIntelligence</c> is
/// false (which exercises the "no geo data" branch that hides the whole panel).
/// </summary>
internal sealed class PlayersDetailsScenario
{
    public PlayersDetailsScenario(
        PlayerDto player,
        bool includeIntelligence = true,
        int riskScore = 0,
        bool isProxy = false,
        bool isVpn = false,
        string proxyType = "",
        string countryCode = "US",
        string cityName = "New York")
    {
        Player = player;

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        GeoLocationClient = new Mock<IGeoLocationApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };

        Mock.Get(RepositoryClient.Object.Players.V1)
            .Setup(api => api.GetPlayer(It.IsAny<Guid>(), It.IsAny<PlayerEntityOptions>()))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.OK, new ApiResponse<PlayerDto>(Player)));

        if (includeIntelligence)
        {
            var intelligence = CreateIntelligence(riskScore, isProxy, isVpn, proxyType, countryCode, cityName);

            Mock.Get(GeoLocationClient.Object.GeoLookup.V1_1)
                .Setup(api => api.GetIpIntelligence(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<IpIntelligenceDto>(HttpStatusCode.OK, new ApiResponse<IpIntelligenceDto>(intelligence)));
        }
        else
        {
            Mock.Get(GeoLocationClient.Object.GeoLookup.V1_1)
                .Setup(api => api.GetIpIntelligence(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<IpIntelligenceDto>(HttpStatusCode.NotFound));
        }
    }

    public PlayerDto Player { get; }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public Mock<IGeoLocationApiClient> GeoLocationClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);

        services.RemoveAll<IGeoLocationApiClient>();
        services.AddSingleton(GeoLocationClient.Object);
    }

    private static IpIntelligenceDto CreateIntelligence(int riskScore, bool isProxy, bool isVpn, string proxyType, string countryCode, string cityName)
    {
        return JsonConvert.DeserializeObject<IpIntelligenceDto>(JsonConvert.SerializeObject(new
        {
            Address = "203.0.113.10",
            CountryCode = countryCode,
            CountryName = "United States",
            CityName = cityName,
            Latitude = 40.7128,
            Longitude = -74.0060,
            MaxMindStatus = SourceStatus.Success.ToString(),
            ProxyCheckStatus = SourceStatus.Success.ToString(),
            ProxyCheck = new
            {
                Address = "203.0.113.10",
                RiskScore = riskScore,
                IsProxy = isProxy,
                IsVpn = isVpn,
                ProxyType = proxyType,
            },
        }))!;
    }
}
