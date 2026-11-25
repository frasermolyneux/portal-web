using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WelcomeExecutionsController(
    IRepositoryApiClient repository,
    IAuthorizationService authorization,
    ILogger<WelcomeExecutionsController> logger) : Controller
{
    private const int PageSize = 20;

    [HttpGet]
    public async Task<IActionResult> Index(Guid id, DateTime? fromUtc, DateTime? toUtc, int page = 0, CancellationToken cancellationToken = default)
    {
        var response = await repository.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
        if (response.IsNotFound)
            return NotFound();
        if (!response.IsSuccess || response.Result?.Data is not { } server || server.GameServerId != id)
            return Unavailable(id);
        if (!(await authorization.AuthorizeAsync(User, server.GameType, AuthPolicies.GameServers_Write).ConfigureAwait(false)).Succeeded)
            return Forbid();

        var to = toUtc.HasValue ? DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        if (!fromUtc.HasValue && to < DateTime.MinValue.AddDays(1))
            return BadRequest();
        var from = fromUtc.HasValue ? DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc) : to.AddDays(-1);
        if (!ModelState.IsValid || page < 0 || page >= int.MaxValue / PageSize || from >= to)
            return BadRequest();

        var executions = await repository.RuleExecutions.V1.SearchRuleExecutions(new RuleFeatureExecutionQueryDto
        {
            GameServerId = id,
            FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature,
            FromUtc = from,
            ToUtc = to,
            SkipEntries = page * PageSize,
            TakeEntries = PageSize
        }, cancellationToken).ConfigureAwait(false);
        if (!executions.IsSuccess || executions.Result?.Data?.Items is null)
            return Unavailable(id);

        var items = executions.Result.Data.Items.ToArray();
        if (items.Any(item => item.GameServerId != id || item.FeatureId != RulesEngineCatalogueV1.WelcomeMessagesFeature))
            return Unavailable(id);

        return View(new WelcomeExecutionsViewModel
        {
            Server = server,
            FromUtc = from,
            ToUtc = to,
            Page = page,
            HasNext = items.Length == PageSize && page + 1 < int.MaxValue / PageSize,
            Executions = items
        });
    }

    private StatusCodeResult Unavailable(Guid serverId)
    {
        logger.LogWarning("Welcome execution diagnostics unavailable for server {ServerId}", serverId);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed class WelcomeExecutionsViewModel
{
    public required GameServerDto Server { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int Page { get; init; }
    public bool HasNext { get; init; }
    public RuleFeatureExecutionDto[] Executions { get; init; } = [];
}
