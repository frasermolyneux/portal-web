using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

internal sealed class ServerFeedScenario
{
    private readonly static JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public ServerFeedScenario()
    {
        GameServerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        GameServer = CreateGameServer(GameServerId);
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        AgentTelemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        AgentTelemetry
            .Setup(service => service.GetServerStatusAsync(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentServerStatus { ActivityStatus = AgentActivityStatus.Offline });
    }

    public Mock<IAgentTelemetryService> AgentTelemetry { get; }
    public GameServerDto GameServer { get; }
    public Guid GameServerId { get; }
    public ConcurrentQueue<FeedResponsePlan> Plans { get; } = new();
    public ConcurrentQueue<Uri> Requests { get; } = new();
    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IAgentTelemetryService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(AgentTelemetry.Object);
    }

    public void QueueResponse(int delayMilliseconds = 0, bool overrun = false, params FeedItem[] items)
    {
        items = [.. items.OrderByDescending(item => item.TimestampUtc).ThenBy(item => item.SourceType, StringComparer.Ordinal).ThenBy(item => item.ItemId, StringComparer.Ordinal)];
        var latest = items.FirstOrDefault();
        var latestChat = items.FirstOrDefault(item => item.SourceType == "chat");
        var latestEvent = items.FirstOrDefault(item => item.SourceType == "event");
        var response = new
        {
            items,
            cursor = new
            {
                lastSeenTimestampUtc = latest?.TimestampUtc,
                lastSeenSourceType = latest?.SourceType,
                lastSeenItemId = latest?.ItemId,
                lastChatMessageId = ParseItemGuid(latestChat?.ItemId),
                lastEventId = ParseItemGuid(latestEvent?.ItemId),
            },
            sourceAuthorization = new { chatAllowed = true, eventsAllowed = true },
            diagnostics = new
            {
                chatCount = items.Count(item => item.SourceType == "chat"),
                eventCount = items.Count(item => item.SourceType == "event"),
                overrunDetected = overrun,
            },
            serverTimeUtc = DateTime.UtcNow.ToString("O"),
        };

        Plans.Enqueue(new FeedResponsePlan(System.Text.Json.JsonSerializer.Serialize(response, jsonOptions), delayMilliseconds));
    }

    public static FeedItem Chat(string itemId, string message, string username = "FeedPlayer")
    {
        return new FeedItem(itemId, "chat", DateTime.UtcNow, message, username, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), null, null, false);
    }

    public static FeedItem Event(string itemId, string eventType, string rawEventData = "{}")
    {
        return new FeedItem(itemId, "event", DateTime.UtcNow, eventType, "Feed Server", null, eventType, rawEventData, false);
    }

    private static Guid? ParseItemGuid(string? itemId)
    {
        return Guid.TryParse(itemId?.Split(':').LastOrDefault(), out var id) ? id : null;
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "Live Feed CoD4 Server",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.8",
            QueryPort = 28966,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 7,
        }))!;
    }

    internal sealed record FeedResponsePlan(string Json, int DelayMilliseconds);

    internal sealed record FeedItem(
        string ItemId,
        string SourceType,
        DateTime TimestampUtc,
        string DisplayText,
        string? Username,
        Guid? PlayerId,
        string? EventType,
        string? RawEventData,
        bool Locked);
}
