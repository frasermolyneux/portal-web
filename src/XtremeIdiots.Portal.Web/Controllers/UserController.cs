using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing user accounts, profiles, and permissions within the XtremeIdiots Portal
/// </summary>
/// <remarks>
/// Initializes a new instance of the UserController
/// </remarks>
/// <param name="authorizationService">Service for checking user authorization policies</param>
/// <param name="repositoryApiClient">Client for accessing the repository API</param>
/// <param name="userManager">ASP.NET Identity user manager for user operations</param>
/// <param name="telemetryClient">Application Insights telemetry client</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize(Policy = AuthPolicies.AccessUsers)]
public class UserController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    UserManager<IdentityUser> userManager,
    TelemetryClient telemetryClient,
    ILogger<UserController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the user management index page
    /// </summary>
    /// <returns>The user index view</returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return View();
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the user permissions page
    /// </summary>
    /// <returns>The permissions view</returns>
    [HttpGet]
    public async Task<IActionResult> Permissions()
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return View();
        }, nameof(Permissions)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the user profile management page for the specified user
    /// </summary>
    /// <param name="id">The user profile ID to manage</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The manage profile view with user data and available game servers</returns>
    [HttpGet]
    public async Task<IActionResult> ManageProfile(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin];
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItems(requiredClaims);

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, gameServerIds, null, 0, 50, GameServerOrder.BannerServerListPosition, cancellationToken).ConfigureAwait(false);

            var userProfileDtoApiResponse = await repositoryApiClient.UserProfiles.V1.GetUserProfile(id, cancellationToken).ConfigureAwait(false);

            if (userProfileDtoApiResponse.IsNotFound)
            {
                Logger.LogWarning("User profile {ProfileId} not found when managing profile", id);
                return NotFound();
            }

            if (gameServersApiResponse.Result?.Data?.Items is null || userProfileDtoApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Invalid API response when managing profile {ProfileId}", id);
                return BadRequest();
            }

            ViewData["GameServers"] = gameServersApiResponse.Result.Data.Items;
            ViewData["GameServersSelect"] = new SelectList(gameServersApiResponse.Result.Data.Items, "GameServerId", "Title");

            // Identity user ID in this system corresponds to the forum id (string). Fallback to profile guid if needed.
            var profileData = userProfileDtoApiResponse.Result.Data;
            IdentityUser? identityUser = null;
            if (profileData.XtremeIdiotsForumId is not null)
            {
                identityUser = await userManager.FindByIdAsync(profileData.XtremeIdiotsForumId.ToString()!).ConfigureAwait(false);
            }

            identityUser ??= await userManager.FindByIdAsync(profileData.UserProfileId.ToString()).ConfigureAwait(false);

            var identitySummary = identityUser is null ? null : new IdentityUserSummary
            {
                Id = identityUser.Id,
                EmailConfirmed = identityUser.EmailConfirmed,
                LockoutEnabled = identityUser.LockoutEnabled,
                LockoutEnd = identityUser.LockoutEnd,
                AccessFailedCount = identityUser.AccessFailedCount,
                TwoFactorEnabled = identityUser.TwoFactorEnabled,
                PhoneNumber = identityUser.PhoneNumber,
                PhoneNumberConfirmed = identityUser.PhoneNumberConfirmed
            };

            var vm = new ManageUserProfileViewModel
            {
                Profile = userProfileDtoApiResponse.Result.Data,
                Identity = identitySummary
            };

            return View(vm);
        }, nameof(ManageProfile)).ConfigureAwait(false);
    }

    /// <summary>
    /// Forces a user to log out by updating their security stamp
    /// </summary>
    /// <param name="id">The user ID to force logout</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to Index with success/warning message</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogUserOut(string id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Logger.LogWarning("Empty user ID provided for force logout");
                return RedirectToAction(nameof(Index));
            }

            var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);

            if (user is null)
            {
                Logger.LogWarning("Could not find user with ID '{UserId}' for force logout", id);
                this.AddAlertWarning($"Could not find user with XtremeIdiots ID '{id}', or there is no user logged in with that XtremeIdiots ID");
                return RedirectToAction(nameof(Index));
            }

            await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

            this.AddAlertSuccess($"User {user.UserName} has been force logged out (this may take up to 15 minutes)");

            TrackSuccessTelemetry("UserForceLoggedOut", nameof(LogUserOut), new Dictionary<string, string>
            {
                { "TargetUser", user.UserName ?? "" },
                { "TargetUserId", id }
            });

            return RedirectToAction(nameof(Index));
        }, nameof(LogUserOut)).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new claim for a user profile on a specific game server
    /// </summary>
    /// <param name="id">The user profile ID</param>
    /// <param name="claimType">The type of claim to create</param>
    /// <param name="claimValue">The game server ID for the claim</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to ManageProfile with success message</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUserClaim(Guid id, string claimType, string claimValue, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileResponseDto = await repositoryApiClient.UserProfiles.V1.GetUserProfile(id, cancellationToken).ConfigureAwait(false);

            if (userProfileResponseDto.IsNotFound)
            {
                Logger.LogWarning("User profile {ProfileId} not found when creating user claim", id);
                return NotFound();
            }

            if (userProfileResponseDto.Result?.Data is null)
            {
                Logger.LogWarning("User profile data is null for {ProfileId}", id);
                return BadRequest();
            }

            var userProfileData = userProfileResponseDto.Result.Data;

            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(Guid.Parse(claimValue), cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {GameServerId} not found when creating user claim", claimValue);
                return NotFound();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.CreateUserClaim,
                nameof(CreateUserClaim),
                "UserClaim",
                $"ProfileId:{id},GameType:{gameServerData.GameType},ClaimType:{claimType}").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            if (!userProfileData.UserProfileClaims.Any(claim => claim.ClaimType == claimType && claim.ClaimValue == claimValue))
            {
                var createUserProfileClaimDto = new CreateUserProfileClaimDto(userProfileData.UserProfileId, claimType, claimValue, false);

                await repositoryApiClient.UserProfiles.V1.CreateUserProfileClaim(
                    userProfileData.UserProfileId, [createUserProfileClaimDto], cancellationToken).ConfigureAwait(false);

                var user = !string.IsNullOrEmpty(userProfileData.XtremeIdiotsForumId)
                    ? await userManager.FindByIdAsync(userProfileData.XtremeIdiotsForumId)
                    : null;

                this.AddAlertSuccess($"The {claimType} claim has been added to {user?.UserName ?? userProfileData.DisplayName}");

                TrackSuccessTelemetry("UserClaimCreated", nameof(CreateUserClaim), new Dictionary<string, string>
                {
                    { "ProfileId", id.ToString() },
                    { "ClaimType", claimType },
                    { "ClaimValue", claimValue },
                    { "GameType", gameServerData.GameType.ToString() }
                });
            }
            else
            {
                var user = !string.IsNullOrEmpty(userProfileData.XtremeIdiotsForumId)
                    ? await userManager.FindByIdAsync(userProfileData.XtremeIdiotsForumId)
                    : null;

                this.AddAlertSuccess($"Nothing to do - {user?.UserName ?? userProfileData.DisplayName} already has the {claimType} claim");
            }

            return RedirectToAction(nameof(ManageProfile), new { id });
        }, nameof(CreateUserClaim), id.ToString());
    }

    /// <summary>
    /// Removes a claim from a user profile
    /// </summary>
    /// <param name="id">The user profile ID</param>
    /// <param name="claimId">The claim ID to remove</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to ManageProfile with success message</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUserClaim(Guid id, Guid claimId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileResponseDto = await repositoryApiClient.UserProfiles.V1.GetUserProfile(id, cancellationToken).ConfigureAwait(false);

            if (userProfileResponseDto.IsNotFound)
            {
                Logger.LogWarning("User profile {ProfileId} not found when removing user claim", id);
                return NotFound();
            }

            if (userProfileResponseDto.Result?.Data is null)
            {
                Logger.LogWarning("User profile data is null for {ProfileId}", id);
                return BadRequest();
            }

            var userProfileData = userProfileResponseDto.Result.Data;
            var claim = userProfileData.UserProfileClaims.SingleOrDefault(c => c.UserProfileClaimId == claimId);

            if (claim is null)
            {
                Logger.LogWarning("Claim {ClaimId} not found for user profile {ProfileId}", claimId, id);
                return NotFound();
            }

            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(Guid.Parse(claim.ClaimValue), cancellationToken).ConfigureAwait(false);

            var canDeleteUserClaim = false;
            if (gameServerApiResponse.IsNotFound)
            {
                Logger.LogInformation("Legacy claim detected for user profile {ProfileId}, allowing deletion", id);
                canDeleteUserClaim = true;
            }
            else if (gameServerApiResponse.Result?.Data is not null)
            {
                var authResult = await CheckAuthorizationAsync(
                    authorizationService,
                    gameServerApiResponse.Result.Data.GameType,
                    AuthPolicies.DeleteUserClaim,
                    nameof(RemoveUserClaim),
                    "UserClaim",
                    $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType}").ConfigureAwait(false);

                if (authResult is not null)
                    return authResult;
                canDeleteUserClaim = true;
            }

            if (!canDeleteUserClaim)
            {
                TrackUnauthorizedAccessAttempt(nameof(RemoveUserClaim), "UserClaim",
                    $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType}");
                return Unauthorized();
            }

            await repositoryApiClient.UserProfiles.V1.DeleteUserProfileClaim(id, claimId, cancellationToken).ConfigureAwait(false);

            var user = !string.IsNullOrEmpty(userProfileData.XtremeIdiotsForumId)
                ? await userManager.FindByIdAsync(userProfileData.XtremeIdiotsForumId)
                : null;

            if (user is not null)
                await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

            this.AddAlertSuccess($"User {userProfileData.DisplayName}'s claim has been removed (this may take up to 15 minutes)");

            TrackSuccessTelemetry("UserClaimRemoved", nameof(RemoveUserClaim), new Dictionary<string, string>
            {
                { "ProfileId", id.ToString() },
                { "ClaimId", claimId.ToString() },
                { "ClaimType", claim.ClaimType },
                { "ClaimValue", claim.ClaimValue }
            });

            return RedirectToAction(nameof(ManageProfile), new { id });
        }, nameof(RemoveUserClaim), id.ToString());
    }
}