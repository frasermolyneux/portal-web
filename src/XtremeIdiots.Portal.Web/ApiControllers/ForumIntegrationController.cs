using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Helpers;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[ApiController]
[Route("api/integration/v1")]
[EnableCors("ForumIntegration")]
[SkipStatusCodePages]
public sealed class ForumIntegrationController(
    MapExperienceService maps, IRepositoryApiClient repository, ILogger<ForumIntegrationController> logger,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("siege-mode")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SiegeMode([FromServices] INamespaceSettingsParser settingsParser, CancellationToken cancellationToken)
    {
        if (!(await authorization.AuthorizeAsync(User, PotentialAccessProbe.Instance, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
            return Forbid();
        var actor = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        var settingsResponse = await repository.GlobalConfigurations.V1
            .GetConfiguration(SiegeModeSettingsConstants.Namespace, cancellationToken).ConfigureAwait(false);
        var settings = new SiegeModeSettingsDocument();
        if (!settingsResponse.IsNotFound)
        {
            if (!settingsResponse.IsSuccess || settingsResponse.Result?.Data is not { } config)
            {
                logger.LogWarning("Forum Siege settings unavailable: {StatusCode}", settingsResponse.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!settingsParser.TryParseSiegeModeSettings(config, out var parsedSettings, out var errors) || parsedSettings is null)
            {
                logger.LogWarning("Forum Siege settings invalid: {Errors}", string.Join("; ", errors));
                return UnprocessableEntity();
            }

            settings = parsedSettings;
        }

        const int pageSize = 200;
        var servers = new List<GameServerDto>();
        var total = -1;
        do
        {
            var response = await repository.GameServers.V1.GetGameServers(null, null, null, servers.Count, pageSize,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data?.Items is not { } items ||
                (response.Result.Pagination?.FilteredCount ?? response.Result.Pagination?.TotalCount) is not { } count || count < 0)
            {
                logger.LogWarning("Forum Siege server list unavailable: {StatusCode}", response.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (servers.Count > 0 && count != total)
            {
                logger.LogWarning("Forum Siege server count changed during pagination");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            total = count;
            var page = items.ToArray();
            if (page.Length == 0 && servers.Count < total)
            {
                logger.LogWarning("Forum Siege server list ended before all pages were loaded");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            servers.AddRange(page);
        } while (servers.Count < total);
        if (servers.Count != total || servers.Select(server => server.GameServerId).Distinct().Count() != total)
        {
            logger.LogWarning("Forum Siege server list contained duplicate servers or an unexpected count");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var rows = new List<object>();
        foreach (var server in servers)
        {
            if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
                continue;

            var current = await repository.SiegeMode.V1.GetCurrentSession(server.GameServerId, cancellationToken).ConfigureAwait(false);
            if (!current.IsNotFound && (!current.IsSuccess || current.Result?.Data is null))
            {
                logger.LogWarning("Forum Siege session unavailable for {ServerId}: {StatusCode}", server.GameServerId, current.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var session = current.Result?.Data;
            rows.Add(new
            {
                server.GameServerId,
                name = CodColorHelper.PlainText(server.Title),
                gameType = server.GameType.ToString(),
                canActivate = session is null,
                currentSession = session is null ? null : new
                {
                    session.SessionId,
                    session.StartedAtUtc,
                    session.ExpiresAtUtc,
                    mode = session.Mode.ToString()
                },
                portalUrl = Url.Action("ServerDetail", "ServerAdmin", new { id = server.GameServerId })
            });
        }

        return Ok(new { portalUser = new { displayName = User.Username() ?? actor }, defaultDurationMinutes = settings.DurationMinutes, servers = rows });
    }

    [HttpPost("siege-mode/servers/{gameServerId:guid}/activate")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ActivateSiegeMode(Guid gameServerId, SiegeActivationRequest request, CancellationToken cancellationToken)
    {
        if (request.Mode is not ("DryRun" or "Enforce") || request.ExpectedDurationMinutes <= 0)
            return BadRequest();

        var serverResponse = await repository.GameServers.V1.GetGameServer(gameServerId, cancellationToken).ConfigureAwait(false);
        if (serverResponse.IsNotFound)
            return NotFound();
        if (!serverResponse.IsSuccess || serverResponse.Result?.Data is not { } server || server.GameServerId != gameServerId)
        {
            logger.LogWarning("Forum Siege server lookup failed for {ServerId}: {StatusCode}", gameServerId, serverResponse.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
            return Forbid();
        var actor = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        var response = await repository.SiegeMode.V1.StartSession(gameServerId, new StartSiegeSessionDto
        {
            OperationId = Guid.NewGuid(),
            Actor = actor,
            Mode = request.Mode == "DryRun" ? SiegeSessionMode.DryRun : SiegeSessionMode.Enforce,
            ExpectedDurationMinutes = request.ExpectedDurationMinutes
        }, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess && response.Result?.Data is { } session)
            return Ok(new { session.SessionId, session.StartedAtUtc, session.ExpiresAtUtc, mode = session.Mode.ToString() });
        if (response.StatusCode == HttpStatusCode.Conflict)
            return Conflict();
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            logger.LogWarning("Forum Siege activation rejected invalid global settings for {ServerId}", gameServerId);
            return UnprocessableEntity();
        }

        if (response.IsNotFound)
            return NotFound();
        logger.LogWarning("Forum Siege activation failed for {ServerId}: {StatusCode}", gameServerId, response.StatusCode);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    public sealed record SiegeActivationRequest(string? Mode, int ExpectedDurationMinutes);

    [HttpGet("session")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Session([FromServices] IAntiforgery antiforgery,
        [FromServices] IAuthorizationService authorizationService)
    {
        var authenticated = User.Identity?.IsAuthenticated == true;
        var players = authenticated &&
            (await authorizationService.AuthorizeAsync(User, HttpContext, AuthPolicies.Players_Read).ConfigureAwait(false)).Succeeded;
        var serverAdmin = authenticated &&
            (await authorizationService.AuthorizeAsync(User, HttpContext, AuthPolicies.GameServers_Admin_Read).ConfigureAwait(false)).Succeeded;
        var chatLogs = serverAdmin &&
            (await authorizationService.AuthorizeAsync(User, HttpContext, AuthPolicies.ChatLog_Read).ConfigureAwait(false)).Succeeded;

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new
        {
            authenticated,
            requestVerificationToken = tokens.RequestToken,
            headerName = tokens.HeaderName,
            capabilities = new { players, serverAdmin, chatLogs }
        });
    }

    [HttpGet("player-links/linked-players")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LinkedPlayers(int skip = 0, int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 100)
            return BadRequest();

        var (profileId, error) = await ResolveProfile(cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;

        var response = await repository.ConnectedPlayers.V1
            .GetConnectedPlayersByUserProfile(profileId, skip, take, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is not { } items)
        {
            logger.LogWarning("Forum linked-player lookup failed: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var rows = items.Select(player => new LinkedPlayerItem(
            player.ConnectedPlayerProfileId,
            player.PlayerId,
            player.GameType.ToString(),
            player.Username,
            player.LinkMethod.ToString(),
            player.LinkedAtUtc,
            player.UnlinkedAtUtc,
            player.IsActive)).ToArray();

        return Ok(new LinkedPlayerPage(rows, skip, take,
            response.Result.Pagination?.TotalCount,
            response.Result.Pagination?.FilteredCount));
    }

    [HttpGet("player-links/claims")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PendingPlayerClaims(int skip = 0, int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 100)
            return BadRequest();

        var (profileId, error) = await ResolveProfile(cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;

        var response = await repository.ConnectedPlayers.V1.GetPendingConnectedPlayerClaims(profileId, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is not { } claims)
        {
            logger.LogWarning("Forum pending-player claims lookup failed: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(new
        {
            skip,
            take,
            totalCount = claims.Count(),
            claims = claims.Skip(skip).Take(take).Select(claim => new
            {
                claim.ClaimId,
                claim.PlayerId,
                claim.Username,
                gameType = claim.GameType.ToString(),
                claim.GameServerId,
                claim.GameServerName,
                claim.Status,
                claim.CreatedAtUtc,
                claim.ExpiresAtUtc
            })
        });
    }

    [HttpPost("player-links/claim-code")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> IssuePlayerClaimCode(CancellationToken cancellationToken)
    {
        var (profileId, error) = await ResolveProfile(cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;

        var response = await repository.ConnectedPlayers.V1.IssueConnectedPlayerClaimCode(
            new ActivateConnectedPlayerActivationCodeDto { UserProfileId = profileId }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data is not { } code || string.IsNullOrWhiteSpace(code.Code))
        {
            logger.LogWarning("Forum player claim code issuance failed: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(new { code.Code, code.ActivatedAtUtc, code.ExpiresAtUtc });
    }

    [HttpPost("player-links/claims/{claimId:guid}/approve")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> ApprovePlayerClaim(Guid claimId, CancellationToken cancellationToken)
        => ResolvePlayerClaim(claimId, true, cancellationToken);

    [HttpPost("player-links/claims/{claimId:guid}/decline")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> DeclinePlayerClaim(Guid claimId, CancellationToken cancellationToken)
        => ResolvePlayerClaim(claimId, false, cancellationToken);

    [HttpDelete("player-links/linked-players/{connectedPlayerProfileId:guid}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UnlinkPlayer(Guid connectedPlayerProfileId, CancellationToken cancellationToken)
    {
        var (profileId, error) = await ResolveProfile(cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;

        var response = await repository.ConnectedPlayers.V1.UnlinkOwnConnectedPlayer(
            profileId, connectedPlayerProfileId, cancellationToken).ConfigureAwait(false);
        return response.IsSuccess ? NoContent() : RepositoryMutationError(response.StatusCode);
    }

    private async Task<IActionResult> ResolvePlayerClaim(Guid claimId, bool approve, CancellationToken cancellationToken)
    {
        var (profileId, error) = await ResolveProfile(cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;

        var response = approve
            ? await repository.ConnectedPlayers.V1.ApproveConnectedPlayerClaim(profileId, claimId, cancellationToken).ConfigureAwait(false)
            : await repository.ConnectedPlayers.V1.DeclineConnectedPlayerClaim(profileId, claimId, cancellationToken).ConfigureAwait(false);
        return response.IsSuccess ? NoContent() : RepositoryMutationError(response.StatusCode);
    }

    private IActionResult RepositoryMutationError(HttpStatusCode status)
    {
        if (status == HttpStatusCode.NotFound)
            return NotFound();
        if (status == HttpStatusCode.Conflict)
            return Conflict();
        logger.LogWarning("Forum player-link mutation failed: {StatusCode}", status);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private async Task<(Guid ProfileId, IActionResult? Error)> ResolveProfile(CancellationToken cancellationToken)
    {
        var xtremeIdiotsId = User.XtremeIdiotsId();
        if (string.IsNullOrWhiteSpace(xtremeIdiotsId))
            return (Guid.Empty, Forbid());

        var response = await repository.UserProfiles.V1
            .GetUserProfileByXtremeIdiotsId(xtremeIdiotsId, cancellationToken).ConfigureAwait(false);
        if (response.IsNotFound)
            return (Guid.Empty, NotFound());
        if (!response.IsSuccess || response.Result?.Data is not { } profile)
        {
            logger.LogWarning("Forum player-link profile lookup failed: {StatusCode}", response.StatusCode);
            return (Guid.Empty, StatusCode(StatusCodes.Status503ServiceUnavailable));
        }

        return profile.ArchivedAtUtc is null
            ? (profile.UserProfileId, null)
            : (Guid.Empty, Forbid());
    }

    public sealed record LinkedPlayerItem(
        Guid ConnectedPlayerProfileId,
        Guid PlayerId,
        string GameType,
        string Username,
        string LinkMethod,
        DateTime LinkedAtUtc,
        DateTime? UnlinkedAtUtc,
        bool IsActive);

    public sealed record LinkedPlayerPage(
        IReadOnlyList<LinkedPlayerItem> Items,
        int Skip,
        int Take,
        int? TotalCount,
        int? FilteredCount);

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

    [HttpGet("server-admin")]
    [Authorize(Policy = AuthPolicies.GameServers_Admin_Read)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ServerAdmin(CancellationToken cancellationToken)
    {
        var result = await new ServerAdminIndexService(repository, authorization).Load(User, cancellationToken).ConfigureAwait(false);
        if (result is null || !result.LiveAvailable)
        {
            logger.LogWarning("Forum server administration data is unavailable from a dependency");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var servers = result.Servers.Select(row => new
        {
            title = CodColorHelper.PlainText(string.IsNullOrWhiteSpace(row.LiveStatus?.Title) ? row.GameServer.Title : row.LiveStatus.Title),
            gameType = row.GameServer.GameType.ToString(),
            hostname = row.GameServer.Hostname,
            queryPort = row.GameServer.QueryPort,
            currentPlayers = row.LiveStatus?.CurrentPlayers,
            maxPlayers = row.LiveStatus?.MaxPlayers,
            currentMap = row.LiveStatus?.Map,
            mod = row.LiveStatus?.Mod,
            consoleUrl = Url.Action("ServerDetail", "ServerAdmin", new { id = row.GameServer.GameServerId }),
            chatLogUrl = row.CanViewChatLog ? Url.Action("ServerChatLog", "ServerAdmin", new { id = row.GameServer.GameServerId }) : null,
            manageMapsUrl = row.CanManageMaps ? Url.Action("Manage", "MapManager", new { id = row.GameServer.GameServerId }) : null
        });
        return Ok(new { servers });
    }

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

        var items = new List<ModerationItem>();
        foreach (var action in response.Result.Data.Items)
        {
            items.Add(new ModerationItem(
                action.AdminActionId, DateTime.SpecifyKind(action.Created, DateTimeKind.Utc),
                action.Player?.GameType.ToString(), action.Type.ToString(), action.Player?.Username,
                action.PlayerId, action.UserProfile?.DisplayName, action.Source.ToString(),
                await GetModerationActionUrl(action).ConfigureAwait(false)));
        }

        return Ok(new ModerationPage(items,
            skip, take, response.Result.Pagination?.TotalCount, response.Result.Pagination?.FilteredCount));
    }

    private async Task<string?> GetModerationActionUrl(AdminActionDto action)
    {
        if (action.Player is not { } player)
            return null;

        var resource = (player.GameType, action.Type, action.UserProfile?.XtremeIdiotsForumId);
        var access = await authorization.AuthorizeAsync(User, resource, AuthPolicies.AdminActions_Edit).ConfigureAwait(false);
        return access.Succeeded ? Url.Action("Edit", "AdminActions", new { id = action.AdminActionId }) : null;
    }

    public sealed record ModerationItem(Guid Id, DateTime CreatedUtc, string? GameType, string Type,
        string? PlayerName, Guid PlayerId, string? AdminName, string Source, string? ActionUrl);
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
        // The grouped query counts the same last-seen records without the overview's separate new/returning queries.
        var active24Task = repository.PlayerAnalyticsV2.V1.GetByGame(now.AddHours(-24), now, cancellationToken);
        var active48Task = repository.PlayerAnalyticsV2.V1.GetByGame(now.AddHours(-48), now, cancellationToken);
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
            !active24.IsSuccess || active24.Result?.Data?.Items is not { } day ||
            !active48.IsSuccess || active48.Result?.Data?.Items is not { } twoDays)
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
            day.Sum(game => game.ActivePlayers), twoDays.Sum(game => game.ActivePlayers), dashboard.UnclaimedBanCount, dashboard.OpenReportCount,
            actions is null ? null : new ActionCounts(actions.Bans, actions.TempBans, actions.Kicks, actions.Warnings, actions.Observations)));
    }

    public sealed record GameOnlineCount(string GameType, int PlayersOnline);
    public sealed record ActionCounts(int Bans, int TempBans, int Kicks, int Warnings, int Observations);
    public sealed record AggregateStats(DateTime AsOfUtc, int PlayersOnline, IReadOnlyList<GameOnlineCount> OnlineByGame,
        int TrackedPlayers, int ActivePlayers24h, int ActivePlayers48h, int UnclaimedBans, int OpenReports,
        ActionCounts? Actions24h);
}
