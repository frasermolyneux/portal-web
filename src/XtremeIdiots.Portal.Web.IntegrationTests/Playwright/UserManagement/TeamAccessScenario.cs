using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.UserManagement;

internal sealed class TeamAccessScenario
{
    private readonly List<UserProfileDto> profiles;

    public TeamAccessScenario()
    {
        var cod4xServer = CreateGameServer(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            GameType.CallOfDuty4x,
            "COD4x Match Server");

        profiles =
        [
            CreateUserProfile(
                Guid.Parse("88888888-8888-8888-8888-888888888888"),
                "Alpha Moderator",
                cod4xServer.GameServerId),
            CreateUserProfile(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                "Bravo Moderator",
                null),
        ];

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };

        Mock.Get(RepositoryClient.Object.UserProfiles.V1)
            .Setup(api => api.GetUserProfiles(
                It.IsAny<string?>(),
                UserProfileFilter.Moderators,
                It.IsAny<GameType?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<UserProfilesOrder>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, UserProfileFilter?, GameType?, int, int, UserProfilesOrder?, CancellationToken>(
                (search, _, gameType, _, _, order, _) =>
                {
                    LastSearch = search;
                    LastGameType = gameType;
                    LastOrder = order;
                    GetUserProfilesCallCount++;
                })
            .ReturnsAsync((string? search, UserProfileFilter? _, GameType? __, int skip, int top, UserProfilesOrder? ___, CancellationToken ____) =>
            {
                var filtered = string.IsNullOrWhiteSpace(search)
                    ? profiles
                    : [.. profiles.Where(profile => (profile.DisplayName ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase))];
                var page = filtered.Skip(skip).Take(top).ToList();

                return new ApiResult<CollectionModel<UserProfileDto>>(
                    HttpStatusCode.OK,
                    new ApiResponse<CollectionModel<UserProfileDto>>(new CollectionModel<UserProfileDto>(page))
                    {
                        Pagination = new ApiPagination
                        {
                            TotalCount = profiles.Count,
                            FilteredCount = filtered.Count,
                            Skip = skip,
                            Top = top,
                        },
                    });
            });

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(
                It.IsAny<GameType[]?>(),
                null,
                null,
                0,
                1000,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<GameType[]?, Guid[]?, GameServerFilter?, int, int, GameServerOrder?, CancellationToken>(
                (gameTypes, _, _, _, _, _, _) => LastRequestedServerGameTypes = gameTypes ?? [])
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([cod4xServer]))));
    }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public int GetUserProfilesCallCount { get; private set; }

    public string? LastSearch { get; private set; }

    public GameType? LastGameType { get; private set; }

    public UserProfilesOrder? LastOrder { get; private set; }

    public IReadOnlyList<GameType> LastRequestedServerGameTypes { get; private set; } = [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }

    private static GameServerDto CreateGameServer(Guid id, GameType gameType, string title)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = title,
            GameType = gameType.ToString(),
            Platform = GameServerPlatform.Windows.ToString(),
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = true,
            FileTransportEnabled = true,
            FileTransportType = "Ftp",
            RconEnabled = true,
        }))!;
    }

    private static UserProfileDto CreateUserProfile(Guid id, string displayName, Guid? cod4xServerId)
    {
        var claims = new List<object>
        {
            new
            {
                UserProfileClaimId = Guid.NewGuid(),
                ClaimType = UserProfileClaimType.Moderator,
                ClaimValue = GameType.CallOfDuty4.ToString(),
                SystemGenerated = true,
            },
            new
            {
                UserProfileClaimId = Guid.NewGuid(),
                ClaimType = AdditionalPermission.MapRotations_Read,
                ClaimValue = GameType.CallOfDuty4x.ToString(),
                SystemGenerated = false,
            },
        };

        if (cod4xServerId.HasValue)
        {
            claims.Add(new
            {
                UserProfileClaimId = Guid.NewGuid(),
                ClaimType = AdditionalPermission.GameServers_Credentials_FileTransport_Read,
                ClaimValue = cod4xServerId.Value.ToString(),
                SystemGenerated = false,
            });
        }

        return JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = id,
            XtremeIdiotsForumId = id.ToString("N"),
            DisplayName = displayName,
            Email = $"{displayName.Replace(" ", ".", StringComparison.Ordinal).ToLowerInvariant()}@example.invalid",
            UserProfileClaims = claims,
        }))!;
    }
}
