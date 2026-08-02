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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

/// <summary>
/// Shapes the repository and geo-location mocks that back <c>/Players</c> (and
/// <c>/Players/GameIndex/{game}</c>) plus the server-side DataTable AJAX endpoint
/// <c>POST /Players/GetPlayersAjax</c>. Each real browser render triggers the DataTable AJAX call, so
/// this scenario returns a deterministic player collection (with pagination) and a deterministic tag
/// list for the filter drop-down. The arguments the controller forwards to
/// <c>IPlayersApi.GetPlayers</c> are captured so tests can prove the game-type / filter-type / tag
/// controls actually flow through to the repository query rather than being silently dropped.
/// </summary>
internal sealed class PlayersIndexScenario
{
    private readonly List<PlayerDto> players;
    private readonly List<TagDto> tags;
    private readonly IpIntelligenceOptions intelligence;
    private readonly Lock gate = new();

    public PlayersIndexScenario(
        IEnumerable<PlayerDto> players,
        IEnumerable<TagDto>? tags = null,
        IpIntelligenceOptions? intelligence = null)
    {
        this.players = [.. players];
        this.tags = tags is null ? [] : [.. tags];
        this.intelligence = intelligence ?? new IpIntelligenceOptions();

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        GeoLocationClient = new Mock<IGeoLocationApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };

        Mock.Get(RepositoryClient.Object.Players.V1)
            .Setup(api => api.GetPlayers(
                It.IsAny<GameType?>(),
                It.IsAny<PlayersFilter?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<PlayersOrder?>(),
                It.IsAny<PlayerEntityOptions>()))
            .Callback<GameType?, PlayersFilter?, string?, int, int, PlayersOrder?, PlayerEntityOptions>(
                (gameType, filter, filterString, skip, top, order, options) =>
                {
                    lock (gate)
                    {
                        GetPlayersCallCount++;
                        LastGameType = gameType;
                        LastFilter = filter;
                        LastFilterString = filterString;
                        LastOrder = order;
                    }
                })
            .ReturnsAsync(() => new ApiResult<CollectionModel<PlayerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>(this.players))
                {
                    Pagination = new ApiPagination
                    {
                        TotalCount = this.players.Count,
                        FilteredCount = this.players.Count,
                        Skip = 0,
                        Top = this.players.Count,
                    },
                }));

        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.GetTags(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<TagDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<TagDto>>(new CollectionModel<TagDto>(this.tags))));

        Mock.Get(GeoLocationClient.Object.GeoLookup.V1_1)
            .Setup(api => api.GetIpIntelligence(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<IpIntelligenceDto>(
                HttpStatusCode.OK,
                new ApiResponse<IpIntelligenceDto>(CreateIntelligence(this.intelligence))));
    }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public Mock<IGeoLocationApiClient> GeoLocationClient { get; }

    public int GetPlayersCallCount { get; private set; }

    public GameType? LastGameType { get; private set; }

    public PlayersFilter? LastFilter { get; private set; }

    public string? LastFilterString { get; private set; }

    public PlayersOrder? LastOrder { get; private set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);

        services.RemoveAll<IGeoLocationApiClient>();
        services.AddSingleton(GeoLocationClient.Object);
    }

    private static IpIntelligenceDto CreateIntelligence(IpIntelligenceOptions options)
    {
        return JsonConvert.DeserializeObject<IpIntelligenceDto>(JsonConvert.SerializeObject(new
        {
            Address = "203.0.113.10",
            CountryCode = options.CountryCode,
            CountryName = "United States",
            CityName = "New York",
            Latitude = 40.7128,
            Longitude = -74.0060,
            MaxMindStatus = SourceStatus.Success.ToString(),
            ProxyCheckStatus = SourceStatus.Success.ToString(),
            ProxyCheck = new
            {
                Address = "203.0.113.10",
                RiskScore = options.RiskScore,
                IsProxy = options.IsProxy,
                IsVpn = options.IsVpn,
                ProxyType = options.ProxyType,
            },
        }))!;
    }
}

/// <summary>
/// Deterministic proxy/geo intelligence returned for every IP in the Players Index AJAX response.
/// </summary>
internal sealed class IpIntelligenceOptions
{
    public int RiskScore { get; set; }

    public bool IsProxy { get; set; }

    public bool IsVpn { get; set; }

    public string ProxyType { get; set; } = "";

    public string CountryCode { get; set; } = "US";
}
