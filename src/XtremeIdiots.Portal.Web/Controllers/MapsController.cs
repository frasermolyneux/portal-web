using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Provides map browsing, search, and image retrieval. The public map browsing
/// endpoints (<c>Index</c>, <c>GameIndex</c>, <c>MapImage</c>) are anonymous —
/// they expose only public map metadata and imagery. Admin-only actions
/// (e.g. <c>VoteLog</c>) carry their own explicit [Authorize] — each action
/// opts in to its own auth posture rather than inheriting from the class.
/// </summary>
/// <remarks>
/// Initializes a new instance of the MapsController
/// </remarks>
/// <param name="repositoryApiClient">Client for accessing repository data</param>
/// <param name="telemetryClient">Client for tracking telemetry data</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
public class MapsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Analytics([FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        try
        {
            var rotations = await maps.Rotations(cancellationToken).ConfigureAwait(false);
            return View(rotations with { Servers = [.. rotations.Servers.Where(server => server.GameType != GameType.Unknown)] });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Map analytics selectors unavailable");
            return StatusCode(503, "Map analytics is temporarily unavailable. Please try again later.");
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Rotations([FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        try
        {
            return View(await maps.BrowsableRotations(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Public map rotations unavailable");
            return StatusCode(503, "Map rotations are temporarily unavailable. Please try again later.");
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SkipStatusCodePages]
    public async Task<IActionResult> Insights([FromServices] MapExperienceService maps, int days = 30,
        GameType? gameType = null, Guid? gameServerId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || days is not (7 or 30 or 90 or 180 or 365) ||
            (gameType.HasValue && (!Enum.IsDefined(gameType.Value) || gameType == GameType.Unknown)))
            return BadRequest();
        if (gameType == GameType.CallOfDuty4x)
            gameType = GameType.CallOfDuty4;
        try
        {
            if (gameServerId.HasValue)
            {
                var server = (await maps.Rotations(cancellationToken).ConfigureAwait(false)).Servers
                    .SingleOrDefault(s => s.GameServerId == gameServerId);
                if (server is null)
                    return NotFound();
                var family = server.GameType == GameType.CallOfDuty4x ? GameType.CallOfDuty4 : server.GameType;
                if (gameType.HasValue && gameType != family)
                    return BadRequest();
            }

            return Json(await maps.Insights(days, gameType, gameServerId, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Public map insights unavailable");
            return StatusCode(503, new { message = "Map analytics is temporarily unavailable. Please try again later." });
        }
    }

    /// <summary>
    /// Displays the main maps index page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Maps index view</returns>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index([FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () => View(await maps.Games(cancellationToken).ConfigureAwait(false)), nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the maps index page filtered by game type
    /// </summary>
    /// <param name="id">Game type to filter by</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Maps index view with game type filter applied</returns>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GameIndex(GameType? id, [FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var games = await maps.Games(cancellationToken).ConfigureAwait(false);
            var family = id == GameType.CallOfDuty4x ? GameType.CallOfDuty4 : id;
            ViewData["GameType"] = family.HasValue && games.Games.Contains(family.Value) ? family : null;
            return View(nameof(Index), games);
        }, nameof(GameIndex)).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves and redirects to a map image or returns a default image if not found
    /// </summary>
    /// <param name="gameType">Game type of the map</param>
    /// <param name="mapName">Name of the map</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirect to map image URI or default no-image placeholder</returns>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> MapImage(GameType gameType, string mapName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (gameType == GameType.Unknown || string.IsNullOrWhiteSpace(mapName))
            {
                Logger.LogWarning("Invalid map image request with game type {GameType} and map name {MapName} from user {UserId}",
                    gameType, mapName ?? "null", User.XtremeIdiotsId());
                return BadRequest();
            }

            var mapApiResponse = await repositoryApiClient.Maps.V1.GetMap(gameType, mapName).ConfigureAwait(false);

            if (!mapApiResponse.IsSuccess || mapApiResponse.Result?.Data is null || string.IsNullOrWhiteSpace(mapApiResponse.Result.Data.MapImageUri))
            {
                Logger.LogWarning("Map image not found for {GameType} map {MapName} requested by user {UserId}",
                    gameType, mapName, User.XtremeIdiotsId());
                return Redirect("/images/noimage.jpg");
            }

            return Redirect(mapApiResponse.Result.Data.MapImageUri);
        }, nameof(MapImage)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the map vote log/audit page (admin-only)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Vote log view</returns>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.MapRotations_Read)]
    public async Task<IActionResult> VoteLog(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(VoteLog)).ConfigureAwait(false);
    }
}
