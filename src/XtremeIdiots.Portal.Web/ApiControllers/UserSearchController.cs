using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for user search operations (autocomplete)
/// </summary>
[Authorize(Policy = AuthPolicies.PerformUserSearch)]
[Route("UserSearch")]
public class UserSearchController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<UserSearchController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Searches user profiles by display name returning lightweight id/text pairs for autocomplete
    /// </summary>
    /// <param name="term">Partial display name (min 2 chars)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON array of objects: { id, text }</returns>
    [HttpGet("Users")]
    public async Task<IActionResult> Users(string? term, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var search = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            if (search is null || search.Length < 2)
                return Ok(Array.Empty<object>());

            var response = await repositoryApiClient.UserProfiles.V1.GetUserProfiles(
                search, UserProfileFilter.AnyAdmin, 0, 15, UserProfilesOrder.DisplayNameAsc, cancellationToken);

            if (!response.IsSuccess || response.Result?.Data?.Items is null)
            {
                Logger.LogWarning("UserSearch.Users failed for term {Term} by user {UserId}", term, User.XtremeIdiotsId());
                return Ok(Array.Empty<object>());
            }

            var data = response.Result.Data.Items
                .Where(u => !string.IsNullOrWhiteSpace(u.DisplayName))
                .Select(u => new { id = u.XtremeIdiotsForumId ?? u.UserProfileId.ToString(), text = u.DisplayName })
                .ToArray();

            TrackSuccessTelemetry("UserSearchCompleted", nameof(Users), new Dictionary<string, string>
            {
                { "SearchTerm", term ?? string.Empty },
                { "ResultCount", data.Length.ToString() }
            });

            return Ok(data);
        }, nameof(Users));
    }
}
