using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize]
public class SiegeAuditController(
    IRepositoryApiClient repository,
    IAuthorizationService authorization,
    ILogger<SiegeAuditController> logger) : Controller
{
    private const int PageSize = 50;
    private const int MaxServers = 1000;

    [HttpGet]
    public async Task<IActionResult> Sessions(Guid? serverId, DateTime? fromUtc, DateTime? toUtc, int page = 0, CancellationToken cancellationToken = default)
    {
        var (model, error) = await PrepareAsync(serverId, fromUtc, toUtc, page, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (model!.Query.GameServerIds.Length == 0)
            return View(model);

        var response = await repository.SiegeMode.V1.SearchSessions(model.Query, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
            return Unavailable("search sessions");

        model.Sessions = [.. response.Result.Data.Items.Where(s => model.Query.GameServerIds.Contains(s.GameServerId))];
        foreach (var actor in model.Sessions.Select(s => s.StartedBy).Where(s => !string.IsNullOrWhiteSpace(s) && s.All(char.IsAsciiDigit)).Distinct())
        {
            var profile = await repository.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(actor, cancellationToken).ConfigureAwait(false);
            if (profile.IsSuccess && profile.Result?.Data is { } user &&
                string.Equals(user.XtremeIdiotsForumId, actor, StringComparison.Ordinal))
                model.StartingUsers[actor] = user.DisplayName ?? actor;
            else if (!profile.IsNotFound)
                logger.LogWarning("Could not resolve Siege session starting user {Actor}", actor);
        }
        model.HasNext = response.Result.Data.Items.Count() == PageSize && ValidPage(page + 1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Actions(Guid? serverId, DateTime? fromUtc, DateTime? toUtc, int page = 0, CancellationToken cancellationToken = default)
    {
        var (model, error) = await PrepareAsync(serverId, fromUtc, toUtc, page, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return error;
        if (model!.Query.GameServerIds.Length == 0)
            return View(model);

        var actions = await repository.RuleExecutions.V1.SearchRuleActions(new RuleActionAuditQueryDto
        {
            GameServerIds = model.Query.GameServerIds,
            FeatureId = RulesEngineCatalogueV1.SiegeModeFeature,
            FromUtc = model.Query.FromUtc,
            ToUtc = model.Query.ToUtc,
            SkipEntries = model.Query.SkipEntries,
            TakeEntries = model.Query.TakeEntries
        }, cancellationToken).ConfigureAwait(false);
        if (!actions.IsSuccess || actions.Result?.Data?.Items is null)
            return Unavailable("search actions");

        var checks = await repository.SiegeMode.V1.SearchChecks(model.Query, cancellationToken).ConfigureAwait(false);
        if (!checks.IsSuccess || checks.Result?.Data?.Items is null)
            return Unavailable("search checks");

        model.Actions = [.. actions.Result.Data.Items.Where(a => model.Query.GameServerIds.Contains(a.GameServerId) &&
            a.FeatureId == RulesEngineCatalogueV1.SiegeModeFeature)];
        model.Checks = [.. checks.Result.Data.Items.Where(c => model.Query.GameServerIds.Contains(c.GameServerId))];
        model.HasNext = (actions.Result.Data.Items.Count() == PageSize || checks.Result.Data.Items.Count() == PageSize) && ValidPage(page + 1);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Session(
        Guid serverId, Guid sessionId, int eventPage = 0, int actionPage = 0, int checkPage = 0,
        CancellationToken cancellationToken = default)
    {
        var serverResponse = await repository.GameServers.V1.GetGameServer(serverId, cancellationToken).ConfigureAwait(false);
        if (serverResponse.IsNotFound)
            return NotFound();
        if (!serverResponse.IsSuccess || serverResponse.Result?.Data is null || serverResponse.Result.Data.GameServerId != serverId)
            return Unavailable("get session server");

        var server = serverResponse.Result.Data;
        if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
            return Forbid();

        if (!ModelState.IsValid || serverId == Guid.Empty || sessionId == Guid.Empty ||
            !ValidPage(eventPage) || !ValidPage(actionPage) || !ValidPage(checkPage))
            return BadRequest();

        var seen = new HashSet<Guid>();
        SiegeSessionDto? session = null;
        for (var skip = 0; ; skip += PageSize)
        {
            var response = await repository.SiegeMode.V1.GetSessions(serverId, skip, PageSize, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data?.Items is null)
                return Unavailable("get server sessions");

            var items = response.Result.Data.Items.ToArray();
            if (items.Any(item => item.GameServerId != serverId || !seen.Add(item.SessionId)))
                return Unavailable("validate server sessions");

            session = items.FirstOrDefault(item => item.SessionId == sessionId);
            if (session is not null)
                break;
            if (items.Length < PageSize)
                return NotFound();
            if (skip > int.MaxValue - PageSize)
                return Unavailable("paginate server sessions");
        }

        var events = await repository.SiegeMode.V1.GetSessionEvents(serverId, sessionId, eventPage * PageSize, PageSize, cancellationToken).ConfigureAwait(false);
        if (!events.IsSuccess || events.Result?.Data?.Items is null)
            return Unavailable("get session events");
        var actions = await repository.RuleExecutions.V1.SearchRuleActions(new RuleActionAuditQueryDto
        {
            GameServerIds = [serverId],
            FeatureId = RulesEngineCatalogueV1.SiegeModeFeature,
            SiegeSessionId = sessionId,
            SkipEntries = actionPage * PageSize,
            TakeEntries = PageSize
        }, cancellationToken).ConfigureAwait(false);
        if (!actions.IsSuccess || actions.Result?.Data?.Items is null)
            return Unavailable("get session actions");
        var checks = await repository.SiegeMode.V1.GetChecks(serverId, sessionId, checkPage * PageSize, PageSize, cancellationToken).ConfigureAwait(false);
        if (!checks.IsSuccess || checks.Result?.Data?.Items is null)
            return Unavailable("get session checks");

        var eventItems = events.Result.Data.Items.ToArray();
        var actionItems = actions.Result.Data.Items.ToArray();
        var checkItems = checks.Result.Data.Items.ToArray();
        return View(new SiegeSessionDetailViewModel
        {
            Server = server,
            Session = session,
            Events = [.. eventItems.Where(item => item.SessionId == sessionId)],
            Actions = [.. actionItems.Where(item => item.SiegeSessionId == sessionId && item.GameServerId == serverId &&
                item.FeatureId == RulesEngineCatalogueV1.SiegeModeFeature)],
            Checks = [.. checkItems.Where(item => item.SessionId == sessionId && item.GameServerId == serverId)],
            EventPage = eventPage,
            ActionPage = actionPage,
            CheckPage = checkPage,
            HasMoreEvents = eventItems.Length == PageSize && ValidPage(eventPage + 1),
            HasMoreActions = actionItems.Length == PageSize && ValidPage(actionPage + 1),
            HasMoreChecks = checkItems.Length == PageSize && ValidPage(checkPage + 1)
        });
    }

    private async Task<(SiegeAuditViewModel? Model, IActionResult? Error)> PrepareAsync(
        Guid? serverId, DateTime? fromUtc, DateTime? toUtc, int page, CancellationToken cancellationToken)
    {
        var permittedGames = new List<GameType>();
        foreach (var game in Enum.GetValues<GameType>().Distinct())
        {
            if ((await authorization.AuthorizeAsync(User, game, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
                permittedGames.Add(game);
        }

        if (permittedGames.Count == 0)
            return (null, Forbid());

        var servers = new List<GameServerDto>();
        var seen = new HashSet<Guid>();
        for (var skip = 0; ; skip += PageSize)
        {
            var response = await repository.GameServers.V1.GetGameServers(
                [.. permittedGames], null, null, skip, PageSize, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data?.Items is null)
                return (null, Unavailable("get game servers"));

            var items = response.Result.Data.Items.ToArray();
            foreach (var server in items)
            {
                if (!seen.Add(server.GameServerId))
                    return (null, Unavailable("validate game server pagination"));
                if ((await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Admin_SiegeMode).ConfigureAwait(false)).Succeeded)
                {
                    servers.Add(server);
                    if (servers.Count > MaxServers)
                        return (null, Unavailable("limit authorized game servers"));
                }
            }

            if (items.Length < PageSize)
                break;
            if (skip > int.MaxValue - PageSize)
                return (null, Unavailable("paginate game servers"));
        }

        if (serverId.HasValue && !servers.Any(s => s.GameServerId == serverId.Value))
            return (null, Forbid());

        if (!ValidPage(page) || (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value >= toUtc.Value) || !ModelState.IsValid)
            return (null, BadRequest());

        var ids = servers.Where(s => !serverId.HasValue || s.GameServerId == serverId.Value)
            .Select(s => s.GameServerId).Distinct().ToArray();
        return (new SiegeAuditViewModel
        {
            Servers = servers,
            ServerId = serverId,
            Page = page,
            Query = new SiegeAuditQueryDto
            {
                GameServerIds = ids,
                FromUtc = fromUtc.HasValue ? DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc) : null,
                ToUtc = toUtc.HasValue ? DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc) : null,
                SkipEntries = page * PageSize,
                TakeEntries = PageSize
            }
        }, null);
    }

    private static bool ValidPage(int page) => page >= 0 && page <= int.MaxValue / PageSize;

    private StatusCodeResult Unavailable(string operation)
    {
        logger.LogWarning("Siege audit unavailable: failed to {Operation}", operation);
        return StatusCode(503);
    }
}

public sealed class SiegeAuditViewModel
{
    public List<GameServerDto> Servers { get; set; } = [];
    public Guid? ServerId { get; set; }
    public int Page { get; set; }
    public bool HasNext { get; set; }
    public SiegeAuditQueryDto Query { get; set; } = new();
    public List<SiegeSessionDto> Sessions { get; set; } = [];
    public Dictionary<string, string> StartingUsers { get; } = new(StringComparer.Ordinal);
    public List<RuleActionExecutionDto> Actions { get; set; } = [];
    public List<SiegeCheckDto> Checks { get; set; } = [];
}

public sealed class SiegeSessionDetailViewModel
{
    public required GameServerDto Server { get; set; }
    public required SiegeSessionDto Session { get; set; }
    public List<SiegeSessionEventDto> Events { get; set; } = [];
    public List<RuleActionExecutionDto> Actions { get; set; } = [];
    public List<SiegeCheckDto> Checks { get; set; } = [];
    public int EventPage { get; set; }
    public int ActionPage { get; set; }
    public int CheckPage { get; set; }
    public bool HasMoreEvents { get; set; }
    public bool HasMoreActions { get; set; }
    public bool HasMoreChecks { get; set; }
}
