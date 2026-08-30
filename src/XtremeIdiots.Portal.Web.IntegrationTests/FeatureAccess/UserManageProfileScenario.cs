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

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

internal sealed class UserManageProfileScenario
{
    public UserManageProfileScenario()
    {
        UserProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gameServer = CreateGameServer(Guid.Parse("22222222-2222-2222-2222-222222222222"), GameType.CallOfDuty5, "Route Test CoD5 Server");
        var userProfile = CreateUserProfile(UserProfileId, gameServer.GameServerId);

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(
                It.IsAny<GameType[]?>(),
                It.IsAny<Guid[]?>(),
                It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerOrder>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([gameServer]))));

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(gameServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));

        Mock.Get(RepositoryClient.Object.UserProfiles.V1)
            .Setup(api => api.GetUserProfile(UserProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));
    }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public Guid UserProfileId { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }

    private static GameServerDto CreateGameServer(Guid gameServerId, GameType gameType, string title)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = title,
            GameType = gameType.ToString(),
            Platform = GameServerPlatform.Windows.ToString(),
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = true,
            FileTransportEnabled = true,
            FileTransportType = "Ftp",
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1,
        }))!;
    }

    private static UserProfileDto CreateUserProfile(Guid userProfileId, Guid gameServerId)
    {
        return JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = userProfileId,
            XtremeIdiotsForumId = "12345",
            DisplayName = "Route Test User",
            Email = "route-test@example.invalid",
            UserProfileClaims = new[]
            {
                new
                {
                    UserProfileClaimId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    ClaimType = AdditionalPermission.GameServers_Credentials_Rcon_Read,
                    ClaimValue = gameServerId.ToString(),
                    SystemGenerated = false,
                },
            },
        }))!;
    }
}
