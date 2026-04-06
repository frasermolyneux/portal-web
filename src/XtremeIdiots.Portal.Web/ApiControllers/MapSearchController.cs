using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize(Policy = AuthPolicies.AccessMapRotations)]
[Route("MapSearch")]
public class MapSearchController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapSearchController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    [HttpGet("Maps")]
    public async Task<IActionResult> Maps(string? term, GameType? gameType, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var search = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            if (search is null || search.Length < 2)
                return Ok(Array.Empty<object>());

            var response = await repositoryApiClient.Maps.V1.GetMaps(
                gameType, null, null, search, 0, 20, MapsOrder.MapNameAsc, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess || response.Result?.Data?.Items is null)
                return Ok(Array.Empty<object>());

            var data = response.Result.Data.Items
                .Select(m => new
                {
                    id = m.MapId.ToString(),
                    text = m.MapName,
                    imageUrl = !string.IsNullOrEmpty(m.MapImageUri) ? m.MapImageUri : "/images/noimage.jpg"
                })
                .ToArray();

            return Ok(data);
        }, nameof(Maps)).ConfigureAwait(false);
    }
}
