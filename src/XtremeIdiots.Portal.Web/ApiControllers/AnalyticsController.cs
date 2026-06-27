using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.Analytics;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// JSON endpoints that back the Analytics command centres and the embedded analytics widgets.
/// All time ranges are UTC; the front end is responsible for localised label formatting.
/// </summary>
[Authorize(Policy = AuthPolicies.Dashboard_Read)]
[Route("api/[controller]")]
public class AnalyticsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<AnalyticsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    #region Global

    [HttpGet("global/overview")]
    public Task<IActionResult> GlobalOverview(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetOverview(Utc(from), Utc(to), cancellationToken), nameof(GlobalOverview));
    }

    [HttpGet("global/timeseries")]
    public Task<IActionResult> GlobalTimeseries(
        DateTime from, DateTime to,
        AnalyticsBucket bucket = AnalyticsBucket.OneDay,
        AnalyticsCompareMode compareMode = AnalyticsCompareMode.None,
        int comparePeriods = AnalyticsQueryDefaults.DefaultComparePeriods,
        AnalyticsAlignMode alignMode = AnalyticsAlignMode.None,
        string timezone = "UTC",
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetTimeseries(
            Utc(from), Utc(to), bucket, compareMode, comparePeriods, alignMode, timezone, normalize, cancellationToken), nameof(GlobalTimeseries));
    }

    [HttpGet("global/games")]
    public Task<IActionResult> GlobalGames(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetGameBreakdown(Utc(from), Utc(to), top, cancellationToken), nameof(GlobalGames));
    }

    [HttpGet("global/servers")]
    public Task<IActionResult> GlobalServers(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetServerBreakdown(Utc(from), Utc(to), top, cancellationToken), nameof(GlobalServers));
    }

    [HttpGet("global/players")]
    public Task<IActionResult> GlobalPlayers(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetPlayerActivity(Utc(from), Utc(to), top, cancellationToken), nameof(GlobalPlayers));
    }

    [HttpGet("global/geo")]
    public Task<IActionResult> GlobalGeo(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetGeoDistribution(Utc(from), Utc(to), top, cancellationToken), nameof(GlobalGeo));
    }

    [HttpGet("global/moderation")]
    public Task<IActionResult> GlobalModeration(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GlobalAnalytics.V1.GetModeration(Utc(from), Utc(to), cancellationToken), nameof(GlobalModeration));
    }

    #endregion

    #region Game

    [HttpGet("game/overview")]
    public Task<IActionResult> GameOverview(GameType gameType, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GameAnalytics.V1.GetOverview(gameType, Utc(from), Utc(to), cancellationToken), nameof(GameOverview));
    }

    [HttpGet("game/timeseries")]
    public Task<IActionResult> GameTimeseries(
        GameType gameType, DateTime from, DateTime to,
        AnalyticsBucket bucket = AnalyticsBucket.OneDay,
        AnalyticsCompareMode compareMode = AnalyticsCompareMode.None,
        int comparePeriods = AnalyticsQueryDefaults.DefaultComparePeriods,
        AnalyticsAlignMode alignMode = AnalyticsAlignMode.None,
        string timezone = "UTC",
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GameAnalytics.V1.GetTimeseries(
            gameType, Utc(from), Utc(to), bucket, compareMode, comparePeriods, alignMode, timezone, normalize, cancellationToken), nameof(GameTimeseries));
    }

    [HttpGet("game/servers")]
    public Task<IActionResult> GameServers(GameType gameType, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GameAnalytics.V1.GetServerBreakdown(gameType, Utc(from), Utc(to), top, cancellationToken), nameof(GameServers));
    }

    [HttpGet("game/players")]
    public Task<IActionResult> GamePlayers(GameType gameType, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GameAnalytics.V1.GetPlayerBreakdown(gameType, Utc(from), Utc(to), top, cancellationToken), nameof(GamePlayers));
    }

    [HttpGet("game/maps")]
    public Task<IActionResult> GameMaps(GameType gameType, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.GameAnalytics.V1.GetMapBreakdown(gameType, Utc(from), Utc(to), top, cancellationToken), nameof(GameMaps));
    }

    #endregion

    #region Server

    [HttpGet("server/overview")]
    public Task<IActionResult> ServerOverview(Guid id, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetOverview(id, Utc(from), Utc(to), cancellationToken), nameof(ServerOverview));
    }

    [HttpGet("server/timeseries")]
    public Task<IActionResult> ServerTimeseries(
        Guid id, DateTime from, DateTime to,
        AnalyticsBucket bucket = AnalyticsBucket.OneDay,
        AnalyticsCompareMode compareMode = AnalyticsCompareMode.None,
        int comparePeriods = AnalyticsQueryDefaults.DefaultComparePeriods,
        AnalyticsAlignMode alignMode = AnalyticsAlignMode.None,
        string timezone = "UTC",
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetTimeseries(
            id, Utc(from), Utc(to), bucket, compareMode, comparePeriods, alignMode, timezone, normalize, cancellationToken), nameof(ServerTimeseries));
    }

    [HttpGet("server/players-current")]
    public Task<IActionResult> ServerPlayersCurrent(Guid id, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetPlayersCurrent(id, cancellationToken), nameof(ServerPlayersCurrent));
    }

    [HttpGet("server/events")]
    public Task<IActionResult> ServerEvents(Guid id, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetEventsSummary(id, Utc(from), Utc(to), cancellationToken), nameof(ServerEvents));
    }

    [HttpGet("server/chat")]
    public Task<IActionResult> ServerChat(Guid id, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetChatSummary(id, Utc(from), Utc(to), top, cancellationToken), nameof(ServerChat));
    }

    [HttpGet("server/chat-commands")]
    public Task<IActionResult> ServerChatCommands(Guid id, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetChatCommandsSummary(id, Utc(from), Utc(to), top, cancellationToken), nameof(ServerChatCommands));
    }

    [HttpGet("server/map-rotation")]
    public Task<IActionResult> ServerMapRotation(Guid id, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.ServerAnalytics.V1.GetMapRotationPerformance(id, Utc(from), Utc(to), cancellationToken), nameof(ServerMapRotation));
    }

    #endregion

    #region Player

    [HttpGet("player/overview")]
    public Task<IActionResult> PlayerOverview(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetOverview(Utc(from), Utc(to), cancellationToken), nameof(PlayerOverview));
    }

    [HttpGet("player/timeseries")]
    public Task<IActionResult> PlayerTimeseries(
        DateTime from, DateTime to,
        AnalyticsBucket bucket = AnalyticsBucket.OneDay,
        AnalyticsCompareMode compareMode = AnalyticsCompareMode.None,
        int comparePeriods = AnalyticsQueryDefaults.DefaultComparePeriods,
        AnalyticsAlignMode alignMode = AnalyticsAlignMode.None,
        string timezone = "UTC",
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetTimeseries(
            Utc(from), Utc(to), bucket, compareMode, comparePeriods, alignMode, timezone, normalize, cancellationToken), nameof(PlayerTimeseries));
    }

    [HttpGet("player/top")]
    public Task<IActionResult> PlayerTop(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetTop(Utc(from), Utc(to), top, cancellationToken), nameof(PlayerTop));
    }

    [HttpGet("player/by-game")]
    public Task<IActionResult> PlayerByGame(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetByGame(Utc(from), Utc(to), cancellationToken), nameof(PlayerByGame));
    }

    [HttpGet("player/by-server")]
    public Task<IActionResult> PlayerByServer(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetByServer(Utc(from), Utc(to), top, cancellationToken), nameof(PlayerByServer));
    }

    [HttpGet("player/{playerId:guid}/detail")]
    public Task<IActionResult> PlayerDetail(Guid playerId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetPlayerDetail(playerId, Utc(from), Utc(to), cancellationToken), nameof(PlayerDetail));
    }

    [HttpGet("player/{playerId:guid}/timeseries")]
    public Task<IActionResult> PlayerDetailTimeseries(
        Guid playerId, DateTime from, DateTime to,
        AnalyticsBucket bucket = AnalyticsBucket.OneDay,
        AnalyticsCompareMode compareMode = AnalyticsCompareMode.None,
        int comparePeriods = AnalyticsQueryDefaults.DefaultComparePeriods,
        AnalyticsAlignMode alignMode = AnalyticsAlignMode.None,
        string timezone = "UTC",
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.PlayerAnalyticsV2.V1.GetPlayerTimeseries(
            playerId, Utc(from), Utc(to), bucket, compareMode, comparePeriods, alignMode, timezone, normalize, cancellationToken), nameof(PlayerDetailTimeseries));
    }

    #endregion

    #region Maps

    [HttpGet("maps/overview")]
    public Task<IActionResult> MapsOverview(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetOverview(Utc(from), Utc(to), cancellationToken), nameof(MapsOverview));
    }

    [HttpGet("maps/hotspots")]
    public Task<IActionResult> MapsHotspots(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetHotspots(Utc(from), Utc(to), top, cancellationToken), nameof(MapsHotspots));
    }

    [HttpGet("maps/top-played")]
    public Task<IActionResult> MapsTopPlayed(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetTopPlayed(Utc(from), Utc(to), top, cancellationToken), nameof(MapsTopPlayed));
    }

    [HttpGet("maps/top-voted")]
    public Task<IActionResult> MapsTopVoted(DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetTopVoted(Utc(from), Utc(to), top, cancellationToken), nameof(MapsTopVoted));
    }

    [HttpGet("maps/by-game")]
    public Task<IActionResult> MapsByGame(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetByGame(Utc(from), Utc(to), cancellationToken), nameof(MapsByGame));
    }

    [HttpGet("maps/by-server")]
    public Task<IActionResult> MapsByServer(Guid id, DateTime from, DateTime to, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetByServer(id, Utc(from), Utc(to), top, cancellationToken), nameof(MapsByServer));
    }

    [HttpGet("maps/{mapId:guid}/detail")]
    public Task<IActionResult> MapDetail(Guid mapId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.MapAnalytics.V1.GetMapDetail(mapId, Utc(from), Utc(to), cancellationToken), nameof(MapDetail));
    }

    #endregion

    #region Dashboard

    [HttpGet("dashboard/home")]
    public Task<IActionResult> DashboardHome(DateTime from, DateTime to, AnalyticsBucket bucket = AnalyticsBucket.OneDay, int top = AnalyticsQueryDefaults.DefaultTop, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.DashboardAnalytics.V1.GetHome(Utc(from), Utc(to), bucket, top, cancellationToken), nameof(DashboardHome));
    }

    [HttpGet("dashboard/server")]
    public Task<IActionResult> DashboardServer(Guid id, DateTime from, DateTime to, AnalyticsBucket bucket = AnalyticsBucket.OneDay, CancellationToken cancellationToken = default)
    {
        return Json(() => repositoryApiClient.DashboardAnalytics.V1.GetServer(id, Utc(from), Utc(to), bucket, cancellationToken), nameof(DashboardServer));
    }

    #endregion

    private async Task<IActionResult> Json<T>(Func<Task<ApiResult<T>>> call, string action) where T : class
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var response = await call().ConfigureAwait(false);

            if (!response.IsSuccess || response.Result?.Data is null)
            {
                Logger.LogWarning("Analytics {Action} failed for user {UserId}", action, User.XtremeIdiotsId());
                return StatusCode(500, $"Failed to retrieve analytics data ({action})");
            }

            return Ok(response.Result.Data);
        }, action).ConfigureAwait(false);
    }

    private static DateTime Utc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
