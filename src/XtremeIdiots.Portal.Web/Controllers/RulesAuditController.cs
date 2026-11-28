using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.GlobalSettings_Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RulesAuditController(
    IRepositoryApiClient repository,
    ILogger<RulesAuditController> logger) : Controller
{
    private const int PageSize = 20;
    private const int ServerPageSize = 100;

    [HttpGet]
    public async Task<IActionResult> Index(
        Guid? serverId, string? featureId, string? fromUtc, string? toUtc, int page = 0,
        RuleExecutionMatchStatus? matchStatus = null,
        CancellationToken cancellationToken = default)
    {
        featureId = string.IsNullOrWhiteSpace(featureId) ? null : featureId.Trim();
        if (!ModelState.IsValid || serverId == Guid.Empty || featureId?.Length > 64 || !ValidPage(page) ||
            (matchStatus.HasValue && !Enum.IsDefined(matchStatus.Value)) ||
            !TryParseUtc(fromUtc, out var from) || !TryParseUtc(toUtc, out var to) ||
            (from.HasValue && to.HasValue && from >= to))
            return BadRequest();

        var servers = await LoadServersAsync(cancellationToken).ConfigureAwait(false);
        if (servers is null)
            return Unavailable("load servers");

        var response = await repository.RuleExecutions.V1.SearchRuleExecutionAudit(new RuleExecutionAuditQueryDto
        {
            GameServerId = serverId,
            FeatureId = featureId,
            FromUtc = from,
            ToUtc = to,
            MatchStatus = matchStatus,
            SkipEntries = page * PageSize,
            TakeEntries = PageSize
        }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
            return Unavailable("search executions");

        var items = response.Result.Data.Items.ToArray();
        if (items.Length > PageSize || items.Any(item =>
                (serverId.HasValue && item.GameServerId != serverId) ||
                (featureId is not null && !string.Equals(item.FeatureId, featureId, StringComparison.OrdinalIgnoreCase)) ||
                !Enum.IsDefined(item.MatchStatus) || (matchStatus.HasValue && item.MatchStatus != matchStatus) ||
                (from.HasValue && item.CreatedAtUtc.UtcDateTime < from) ||
                (to.HasValue && item.CreatedAtUtc.UtcDateTime >= to) ||
                item.Diagnostics is null || item.Actions is null || item.PreparedActions is null))
            return Unavailable("validate execution results");

        return View(new RulesAuditViewModel
        {
            Servers = servers,
            ServerId = serverId,
            FeatureId = featureId,
            FromUtc = from,
            ToUtc = to,
            MatchStatus = matchStatus,
            Page = page,
            HasNext = ValidPage(page + 1) && (response.Result.Pagination is { } pagination
                ? (long)page * PageSize + items.Length < pagination.FilteredCount
                : items.Length == PageSize),
            Executions = items
        });
    }

    [HttpGet]
    public async Task<IActionResult> Execution(Guid id, bool fragment = false, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || id == Guid.Empty)
            return BadRequest();

        var response = await repository.RuleExecutions.V1.GetRuleExecutionFlow(id, cancellationToken).ConfigureAwait(false);
        if (response.IsNotFound)
            return NotFound();
        if (!response.IsSuccess || response.Result?.Data is not { } flow ||
            flow.Execution is null || flow.Execution.ExecutionId != id || !Enum.IsDefined(flow.Execution.MatchStatus) ||
            flow.Execution.Diagnostics is null || flow.Execution.Actions is null ||
            flow.Execution.PreparedActions is null || !ValidEvidence(flow.Execution.EvaluationEvidence) || flow.Rules is null ||
            !Enum.IsDefined(flow.StructureStatus) ||
            flow.Rules.Any(rule => rule is null || rule.Condition is null || rule.Actions is null))
            return Unavailable("load execution flow");

        if (fragment)
            return PartialView("_ExecutionFlow", flow);

        return View(flow);
    }

    private static bool ValidEvidence(RuleEvaluationEvidence? evidence) => evidence is null || !evidence.HasSupportedSchema ||
        evidence.Nodes is not null && evidence.ActionDecisions is not null &&
        evidence.Nodes.All(node => node is not null) && evidence.ActionDecisions.All(decision => decision is not null);

    private static bool TryParseUtc(string? value, out DateTime? utc)
    {
        utc = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        utc = parsed.UtcDateTime;
        return true;
    }

    private static bool ValidPage(int page) => page >= 0 && page < int.MaxValue / PageSize;

    private async Task<IReadOnlyList<GameServerDto>?> LoadServersAsync(CancellationToken cancellationToken)
    {
        var servers = new List<GameServerDto>();
        var seen = new HashSet<Guid>();
        for (var skip = 0; ; skip += ServerPageSize)
        {
            var response = await repository.GameServers.V1.GetGameServers(
                null, null, null, skip, ServerPageSize, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data?.Items is null)
                return null;

            var items = response.Result.Data.Items.ToArray();
            if (items.Any(server => server.GameServerId == Guid.Empty || !seen.Add(server.GameServerId)))
                return null;
            servers.AddRange(items);
            if (items.Length < ServerPageSize)
                return servers;
            if (skip > int.MaxValue - ServerPageSize)
                return null;
        }
    }

    private StatusCodeResult Unavailable(string operation)
    {
        logger.LogWarning("Rules engine audit unavailable: failed to {Operation}", operation);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
