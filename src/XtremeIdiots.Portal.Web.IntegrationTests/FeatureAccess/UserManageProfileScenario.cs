using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

internal sealed class UserManageProfileScenario
{
    public UserManageProfileScenario()
    {
        UserProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        NotificationTypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var gameServer = CreateGameServer(Guid.Parse("22222222-2222-2222-2222-222222222222"), GameType.CallOfDuty5, "Route Test CoD5 Server");
        var userProfile = CreateUserProfile(UserProfileId, gameServer.GameServerId);
        var notificationType = CreateNotificationType(NotificationTypeId);
        var notificationPreference = CreateNotificationPreference(NotificationTypeId);
        var notification = CreateNotification(NotificationTypeId);

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

        Mock.Get(RepositoryClient.Object.NotificationTypes.V1)
            .Setup(api => api.GetNotificationTypes(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationTypeDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationTypeDto>>(new CollectionModel<NotificationTypeDto>([notificationType]))));

        Mock.Get(RepositoryClient.Object.NotificationPreferences.V1)
            .Setup(api => api.GetNotificationPreferences(UserProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationPreferenceDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationPreferenceDto>>(new CollectionModel<NotificationPreferenceDto>([notificationPreference]))));

        Mock.Get(RepositoryClient.Object.Notifications.V1)
            .Setup(api => api.GetNotifications(
                UserProfileId,
                null,
                0,
                50,
                NotificationOrder.CreatedAtDesc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationDto>>(new CollectionModel<NotificationDto>([notification]))));

        Mock.Get(RepositoryClient.Object.NotificationPreferences.V1)
            .Setup(api => api.UpdateNotificationPreferences(
                UserProfileId,
                It.IsAny<List<EditNotificationPreferenceDto>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, List<EditNotificationPreferenceDto>, CancellationToken>((_, preferences, _) =>
            {
                UpdatedPreferences = [.. preferences];
                UpdateNotificationPreferencesCallCount++;
            })
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
    }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public Guid UserProfileId { get; }

    public Guid NotificationTypeId { get; }

    public IReadOnlyList<EditNotificationPreferenceDto> UpdatedPreferences { get; private set; } = [];

    public int UpdateNotificationPreferencesCallCount { get; private set; }

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

    private static NotificationTypeDto CreateNotificationType(Guid notificationTypeId)
    {
        return JsonConvert.DeserializeObject<NotificationTypeDto>(JsonConvert.SerializeObject(new
        {
            NotificationTypeId = notificationTypeId.ToString(),
            DisplayName = "Route Notification",
            Description = "Shows route-level notification data.",
            SupportsInSite = true,
            SupportsEmail = true,
            DefaultChannels = "InSite,Email",
        }))!;
    }

    private static NotificationPreferenceDto CreateNotificationPreference(Guid notificationTypeId)
    {
        return JsonConvert.DeserializeObject<NotificationPreferenceDto>(JsonConvert.SerializeObject(new
        {
            NotificationTypeId = notificationTypeId.ToString(),
            InSiteEnabled = true,
            EmailEnabled = false,
        }))!;
    }

    private static NotificationDto CreateNotification(Guid notificationTypeId)
    {
        return JsonConvert.DeserializeObject<NotificationDto>(JsonConvert.SerializeObject(new
        {
            NotificationId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            NotificationTypeId = notificationTypeId.ToString(),
            Title = "Route notification title",
            Message = "Route notification message",
            CreatedAt = DateTime.UtcNow,
            IsRead = false,
            EmailSent = true,
        }))!;
    }
}
