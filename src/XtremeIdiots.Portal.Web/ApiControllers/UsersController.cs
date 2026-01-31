using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for user profile data operations
/// </summary>
[Authorize(Policy = AuthPolicies.AccessUsers)]
[Route("User")]
public class UsersController(
    UserManager<IdentityUser> userManager,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<UsersController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Provides AJAX endpoint for retrieving paginated user data for DataTables
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON response with user data formatted for DataTables</returns>
    [HttpPost("GetUsersAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetUsersAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid request body for users AJAX endpoint from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            UserProfileFilter? userProfileFilter = null;
            var rawFlag = HttpContext.Request.Query["userFlag"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rawFlag) && Enum.TryParse<UserProfileFilter>(rawFlag, true, out var parsedFlag))
            {
                userProfileFilter = parsedFlag;
            }

            var order = UserProfilesOrder.DisplayNameAsc;
            if (model.Order?.Count > 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var dir = model.Order.First().Dir;
                if (orderColumn.Equals("displayName", StringComparison.OrdinalIgnoreCase))
                {
                    order = dir.Equals("asc", StringComparison.OrdinalIgnoreCase)
                        ? UserProfilesOrder.DisplayNameAsc
                        : UserProfilesOrder.DisplayNameDesc;
                }
            }

            var userProfileResponseDto = await repositoryApiClient.UserProfiles.V1.GetUserProfiles(
                model.Search?.Value, userProfileFilter, model.Start, model.Length, order, cancellationToken).ConfigureAwait(false);

            if (userProfileResponseDto.Result?.Data is null)
            {
                Logger.LogWarning("Invalid API response for users AJAX endpoint for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, "Failed to retrieve user profiles data");
            }

            var profileItems = userProfileResponseDto.Result.Data.Items?.ToList() ?? [];
            var idStrings = profileItems.Select(p => p.UserProfileId.ToString()).ToList();
            var identityUsers = userManager.Users
                .Where(u => idStrings.Contains(u.Id))
                .Select(u => new IdentityUserSummary
                {
                    Id = u.Id,
                    EmailConfirmed = u.EmailConfirmed,
                    LockoutEnabled = u.LockoutEnabled,
                    LockoutEnd = u.LockoutEnd,
                    AccessFailedCount = u.AccessFailedCount,
                    TwoFactorEnabled = u.TwoFactorEnabled,
                    PhoneNumber = u.PhoneNumber,
                    PhoneNumberConfirmed = u.PhoneNumberConfirmed
                })
                .ToList();
            var identityLookup = identityUsers.ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);

            var enriched = profileItems.Select(p => new
            {
                p.UserProfileId,
                p.XtremeIdiotsForumId,
                p.DisplayName,
                p.Email,
                p.UserProfileClaims,
                identity = identityLookup.GetValueOrDefault(p.UserProfileId.ToString())
            });

            TrackSuccessTelemetry("UsersListRetrieved", nameof(GetUsersAjax), new Dictionary<string, string>
            {
                { "ResultCount", profileItems.Count.ToString() },
                { "Filter", userProfileFilter?.ToString() ?? "None" }
            });

            return Ok(new
            {
                model.Draw,
                recordsTotal = userProfileResponseDto.Result?.Pagination?.TotalCount,
                recordsFiltered = userProfileResponseDto.Result?.Pagination?.FilteredCount,
                data = enriched
            });
        }, nameof(GetUsersAjax)).ConfigureAwait(false);
    }
}

/// <summary>
/// Summary of identity user properties for enrichment
/// </summary>
public class IdentityUserSummary
{
    public string Id { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
}
