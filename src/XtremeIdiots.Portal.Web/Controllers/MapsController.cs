using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Provides map browsing, search, and image retrieval functionality
/// </summary>
/// <remarks>
/// Initializes a new instance of the MapsController
/// </remarks>
/// <param name="repositoryApiClient">Client for accessing repository data</param>
/// <param name="telemetryClient">Client for tracking telemetry data</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize(Policy = AuthPolicies.AccessMaps)]
public class MapsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapsController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the main maps index page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Maps index view</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the maps index page filtered by game type
    /// </summary>
    /// <param name="id">Game type to filter by</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Maps index view with game type filter applied</returns>
    [HttpGet]
    public async Task<IActionResult> GameIndex(GameType? id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() =>
        {
            ViewData["GameType"] = id;
            return Task.FromResult<IActionResult>(View(nameof(Index)));
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

            TrackSuccessTelemetry("MapImageRetrieved", "MapImage", new Dictionary<string, string>
            {
                { "GameType", gameType.ToString() },
                { "MapName", mapName }
            });

            return Redirect(mapApiResponse.Result.Data.MapImageUri);
        }, nameof(MapImage)).ConfigureAwait(false);
    }
}