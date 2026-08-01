using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.AdminActions;

internal sealed class AdminActionScenario
{
    public AdminActionScenario(
        Guid? playerId = null,
        GameType gameType = GameType.CallOfDuty4,
        string username = "WorkflowPlayer",
        bool createSucceeds = true)
    {
        PlayerId = playerId ?? Guid.Parse("44444444-4444-4444-4444-444444444444");
        Player = CreatePlayer(PlayerId, gameType, username);

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };
        AdminActionTopics = new Mock<IAdminActionTopics>(MockBehavior.Strict);
        NotificationDispatcher = new Mock<INotificationDispatcher>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.Players.V1)
            .Setup(api => api.GetPlayer(PlayerId, It.IsAny<PlayerEntityOptions>()))
            .ReturnsAsync(new ApiResult<PlayerDto>(HttpStatusCode.OK, new ApiResponse<PlayerDto>(Player)));
        Mock.Get(RepositoryClient.Object.AdminActions.V1)
            .Setup(api => api.CreateAdminAction(It.IsAny<CreateAdminActionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateAdminActionDto, CancellationToken>((dto, _) => CreatedAdminActions.Enqueue(dto))
            .ReturnsAsync(new ApiResult(createSucceeds ? HttpStatusCode.Created : HttpStatusCode.InternalServerError));
        AdminActionTopics
            .Setup(topics => topics.CreateTopicForAdminAction(
                It.IsAny<AdminActionType>(),
                It.IsAny<GameType>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(123456);
        NotificationDispatcher
            .Setup(dispatcher => dispatcher.DispatchAdminActionCreatedAsync(
                It.IsAny<AdminActionNotificationContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<AdminActionNotificationContext, CancellationToken>((context, _) => Notifications.Enqueue(context))
            .Returns(Task.CompletedTask);
    }

    public Mock<IAdminActionTopics> AdminActionTopics { get; }

    public System.Collections.Concurrent.ConcurrentQueue<CreateAdminActionDto> CreatedAdminActions { get; } = new();

    public System.Collections.Concurrent.ConcurrentQueue<AdminActionNotificationContext> Notifications { get; } = new();

    public Mock<INotificationDispatcher> NotificationDispatcher { get; }

    public PlayerDto Player { get; }

    public Guid PlayerId { get; }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IAdminActionTopics>();
        services.RemoveAll<INotificationDispatcher>();

        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(AdminActionTopics.Object);
        services.AddSingleton(NotificationDispatcher.Object);
    }

    private static PlayerDto CreatePlayer(Guid playerId, GameType gameType, string username)
    {
        var json = JsonConvert.SerializeObject(new
        {
            PlayerId = playerId,
            GameType = gameType.ToString(),
            Username = username,
            Guid = "WORKFLOW-GUID",
            IpAddress = "127.0.0.1",
            FirstSeen = DateTime.UtcNow.AddDays(-30),
            LastSeen = DateTime.UtcNow,
        });

        return JsonConvert.DeserializeObject<PlayerDto>(json)!;
    }
}
