using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[ApiController]
[Route("api/integration/v1")]
[EnableCors("ForumIntegration")]
[SkipStatusCodePages]
public sealed class ForumIntegrationController(
    MapExperienceService maps, IRepositoryApiClient repository, ILogger<ForumIntegrationController> logger) : ControllerBase
{
    [HttpGet("session")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Session([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { authenticated = User.Identity?.IsAuthenticated == true, requestVerificationToken = tokens.RequestToken, headerName = tokens.HeaderName });
    }

    [HttpGet("rotations")]
    [AllowAnonymous]
    public async Task<IActionResult> Rotations(CancellationToken cancellationToken)
    {
        var rotations = await maps.Rotations(cancellationToken).ConfigureAwait(false);
        return Ok(new { rotations.Servers });
    }

    [HttpGet("maps")]
    [AllowAnonymous]
    public async Task<IActionResult> Maps(GameType? gameType, string? search, int skip = 0, int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 100 || (gameType.HasValue && !Enum.IsDefined(gameType.Value)))
            return BadRequest();
        var response = await repository.Maps.V1.GetMaps(gameType, null, null, search?.Trim(), skip, take,
            MapsOrder.MapNameAsc, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
        {
            logger.LogWarning("Forum map catalogue is unavailable: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(new MapPage(
            [.. response.Result.Data.Items.Select(map => new MapItem(map.MapId, map.GameType.ToString(), map.MapName, map.MapImageUri))],
            skip, take, response.Result.Pagination?.TotalCount, response.Result.Pagination?.FilteredCount));
    }

    public sealed record MapItem(Guid Id, string GameType, string Name, string? ImageUrl);
    public sealed record MapPage(IReadOnlyList<MapItem> Items, int Skip, int Take, int? TotalCount, int? FilteredCount);

    [HttpGet("moderation")]
    [Authorize(Policy = AuthPolicies.AdminActions_Read)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Moderation(GameType? gameType, int skip = 0, int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 100 || (gameType.HasValue && !Enum.IsDefined(gameType.Value)))
            return BadRequest();
        var response = await repository.AdminActions.V1.GetAdminActions(
            gameType, null, null, null, skip, take, AdminActionOrder.CreatedDesc, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
        {
            logger.LogWarning("Forum moderation feed is unavailable: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(new ModerationPage(
            [.. response.Result.Data.Items.Select(action => new ModerationItem(
                action.AdminActionId, DateTime.SpecifyKind(action.Created, DateTimeKind.Utc),
                action.Player?.GameType.ToString(), action.Type.ToString(), action.Player?.Username,
                action.PlayerId, action.UserProfile?.DisplayName, action.Source.ToString()))],
            skip, take, response.Result.Pagination?.TotalCount, response.Result.Pagination?.FilteredCount));
    }

    public sealed record ModerationItem(Guid Id, DateTime CreatedUtc, string? GameType, string Type,
        string? PlayerName, Guid PlayerId, string? AdminName, string Source);
    public sealed record ModerationPage(IReadOnlyList<ModerationItem> Items, int Skip, int Take, int? TotalCount, int? FilteredCount);

    [HttpGet("activity")]
    [Authorize(Policy = AuthPolicies.Users_ActivityLog)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Activity([FromServices] IActivityLogService activity,
        ActivityLogCategory? category, string? search, int hours = 24, int skip = 0, int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (hours is not (1 or 6 or 12 or 24 or 168 or 720) || skip < 0 || take is < 1 or > 100 ||
            (category.HasValue && !Enum.IsDefined(category.Value)))
            return BadRequest();
        var result = await activity.QueryEventsAsync(TimeSpan.FromHours(hours),
            category.HasValue ? [category.Value] : [], [], search, skip, take, "timestamp", "desc", cancellationToken).ConfigureAwait(false);
        return Ok(new ActivityPage(result.Entries, skip, take, result.TotalCount, result.FilteredCount));
    }

    public sealed record ActivityPage(IReadOnlyList<ActivityLogEntry> Items, int Skip, int Take, int TotalCount, int FilteredCount);

    [HttpGet("stats")]
    [Authorize(Policy = AuthPolicies.Dashboard_Read)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var summaryTask = repository.Dashboard.V1.GetDashboardSummary(cancellationToken);
        var serversTask = repository.GameServers.V1.GetGameServers(null, null, null, 0, 200,
            GameServerOrder.ServerListPosition, cancellationToken);
        var liveTask = repository.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken);
        var trackedTask = repository.Players.V1.GetPlayers(null, null, null, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None);
        var active24Task = repository.PlayerAnalyticsV2.V1.GetOverview(now.AddHours(-24), now, cancellationToken);
        var active48Task = repository.PlayerAnalyticsV2.V1.GetOverview(now.AddHours(-48), now, cancellationToken);
        await Task.WhenAll(summaryTask, serversTask, liveTask, trackedTask, active24Task, active48Task).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var servers = await serversTask.ConfigureAwait(false);
        var live = await liveTask.ConfigureAwait(false);
        var tracked = await trackedTask.ConfigureAwait(false);
        var active24 = await active24Task.ConfigureAwait(false);
        var active48 = await active48Task.ConfigureAwait(false);
        if (!summary.IsSuccess || summary.Result?.Data is not { } dashboard ||
            !servers.IsSuccess || servers.Result?.Data?.Items is null ||
            !live.IsSuccess || live.Result?.Data?.Items is null ||
            !tracked.IsSuccess || tracked.Result?.Pagination?.TotalCount is not { } trackedCount ||
            !active24.IsSuccess || active24.Result?.Data is not { } day ||
            !active48.IsSuccess || active48.Result?.Data is not { } twoDays)
        {
            logger.LogWarning("Forum aggregate statistics are unavailable from a dependency");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var serverList = servers.Result.Data.Items.ToList();
        if (servers.Result.Pagination?.TotalCount is not { } serverCount)
        {
            logger.LogWarning("Forum aggregate statistics require a server count");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        while (serverList.Count < serverCount)
        {
            var page = await repository.GameServers.V1.GetGameServers(null, null, null, serverList.Count, 200,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess || page.Result?.Data?.Items is null || !page.Result.Data.Items.Any())
            {
                logger.LogWarning("Forum aggregate statistics cannot load all server pages");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            serverList.AddRange(page.Result.Data.Items);
        }

        if (serverList.Select(server => server.GameServerId).Distinct().Count() != serverList.Count)
        {
            logger.LogWarning("Forum aggregate server pages contain duplicate servers");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var byServer = live.Result.Data.Items.GroupBy(status => status.ServerId)
            .ToDictionary(group => group.Key, group => group.First().CurrentPlayers);
        var byGame = serverList.GroupBy(server => server.GameType)
            .Select(group => new GameOnlineCount(group.Key.ToString(),
                group.Sum(server => byServer.GetValueOrDefault(server.GameServerId))))
            .OrderBy(item => item.GameType).ToArray();

        var actions = dashboard.RecentActions24h;
        return Ok(new AggregateStats(now, dashboard.TotalPlayersOnline, byGame, trackedCount,
            day.ActivePlayers, twoDays.ActivePlayers, dashboard.UnclaimedBanCount, dashboard.OpenReportCount,
            actions is null ? null : new ActionCounts(actions.Bans, actions.TempBans, actions.Kicks, actions.Warnings, actions.Observations)));
    }

    public sealed record GameOnlineCount(string GameType, int PlayersOnline);
    public sealed record ActionCounts(int Bans, int TempBans, int Kicks, int Warnings, int Observations);
    public sealed record AggregateStats(DateTime AsOfUtc, int PlayersOnline, IReadOnlyList<GameOnlineCount> OnlineByGame,
        int TrackedPlayers, int ActivePlayers24h, int ActivePlayers48h, int UnclaimedBans, int OpenReports,
        ActionCounts? Actions24h);
}
