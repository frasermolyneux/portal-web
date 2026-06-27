using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.Observability.ApplicationInsights.Auditing;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels.Analytics;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Top-level Analytics command centre. Renders the Global, Game, Server, Player and Maps
/// analytics pages. The charts are populated client-side via <c>AnalyticsApiController</c>.
/// </summary>
[Authorize(Policy = AuthPolicies.Dashboard_Read)]
public class AnalyticsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<AnalyticsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    private static IReadOnlyList<GameType> SelectableGames { get; } =
        [.. Enum.GetValues<GameType>().Where(g => g != GameType.Unknown)];

    /// <summary>
    /// Landing page linking to each analytics command centre.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Global analytics command centre.
    /// </summary>
    [HttpGet]
    public IActionResult Global()
    {
        return View(new AnalyticsGlobalViewModel());
    }

    /// <summary>
    /// Per-game analytics command centre.
    /// </summary>
    [HttpGet]
    public IActionResult Game(GameType? id)
    {
        var selected = id is GameType gameType && gameType != GameType.Unknown ? gameType : SelectableGames[0];
        return View(new AnalyticsGameViewModel
        {
            Games = SelectableGames,
            SelectedGame = selected
        });
    }

    /// <summary>
    /// Per-server analytics command centre.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Server(Guid? id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var servers = await GetServerOptionsAsync(cancellationToken).ConfigureAwait(false);
            var selected = id;
            if (selected is null && servers.Count > 0)
            {
                selected = servers[0].GameServerId;
            }

            return View(new AnalyticsServerViewModel
            {
                Servers = servers,
                SelectedServerId = selected
            });
        }, nameof(Server)).ConfigureAwait(false);
    }

    /// <summary>
    /// Aggregate player analytics command centre.
    /// </summary>
    [HttpGet]
    public IActionResult Player()
    {
        return View(new AnalyticsPlayerViewModel());
    }

    /// <summary>
    /// Maps analytics command centre.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Maps(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var servers = await GetServerOptionsAsync(cancellationToken).ConfigureAwait(false);

            return View(new AnalyticsMapsViewModel
            {
                Games = SelectableGames,
                Servers = servers
            });
        }, nameof(Maps)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AnalyticsServerOption>> GetServerOptionsAsync(CancellationToken cancellationToken)
    {
        var response = await repositoryApiClient.GameServers.V1.GetGameServers(
            null, null, null, 0, 200, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess || response.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Failed to retrieve game servers for analytics selectors for user {UserId}", User.XtremeIdiotsId());
            return [];
        }

        return [.. response.Result.Data.Items
            .Select(gs => new AnalyticsServerOption(gs.GameServerId, gs.Title ?? gs.GameServerId.ToString(), gs.GameType))
            .OrderBy(o => o.GameType)
            .ThenBy(o => o.Title)];
    }
}
