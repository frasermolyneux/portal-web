using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
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
[Authorize(Policy = AuthPolicies.Users_Read)]
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
    /// Displays the permissions report page showing all assigned permissions
    /// </summary>
    /// <returns>The permissions report view</returns>
    [HttpGet]
    public async Task<IActionResult> PermissionsReport()
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return View();
        }, nameof(PermissionsReport)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the activity log page showing Application Insights custom events
    /// </summary>
    /// <returns>The activity log view</returns>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Users_ActivityLog)]
    public async Task<IActionResult> ActivityLog()
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            await Task.CompletedTask.ConfigureAwait(false);
            return View();
        }, nameof(ActivityLog)).ConfigureAwait(false);
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
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, gameServerIds, null, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

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
                identityUser = await userManager.FindByIdAsync(profileData.XtremeIdiotsForumId.ToString()).ConfigureAwait(false);
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
                Identity = identitySummary,
                AssignableGameTypes = User.GetGameTypesForGameServers()
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
    /// Creates a new claim for a user profile
    /// </summary>
    /// <param name="id">The user profile ID</param>
    /// <param name="claimType">The type of claim to create (must be in AdditionalPermission.AllowedTypes)</param>
    /// <param name="claimValue">The scope value — a GameType name or game server GUID</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to ManageProfile with success message</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUserClaim(Guid id, string claimType, string claimValue, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!AdditionalPermission.IsAllowed(claimType))
            {
                Logger.LogWarning("Invalid claim type '{ClaimType}' attempted for profile {ProfileId}", claimType, id);
                return BadRequest($"Invalid permission type: {claimType}");
            }

            if (string.IsNullOrWhiteSpace(claimValue))
            {
                Logger.LogWarning("Empty claim value for claim type '{ClaimType}' on profile {ProfileId}", claimType, id);
                return BadRequest("A scope value must be provided.");
            }

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

            // Determine the authorization resource based on the claim value type
            object authResource;
            string gameTypeForTelemetry;

            if (Guid.TryParse(claimValue, out var gameServerId))
            {
                // Server-scoped claim — look up the server to get the GameType for auth
                var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(gameServerId, cancellationToken).ConfigureAwait(false);

                if (gameServerApiResponse.Result?.Data is null)
                {
                    Logger.LogWarning("Game server {GameServerId} not found when creating user claim", claimValue);
                    return NotFound();
                }

                authResource = gameServerApiResponse.Result.Data.GameType;
                gameTypeForTelemetry = gameServerApiResponse.Result.Data.GameType.ToString();
            }
            else if (Enum.TryParse<GameType>(claimValue, out var gameType))
            {
                // Game-scoped claim — use the GameType directly for auth
                authResource = gameType;
                gameTypeForTelemetry = gameType.ToString();
            }
            else
            {
                Logger.LogWarning("Invalid claim value '{ClaimValue}' — not a valid GameType or server GUID", claimValue);
                return BadRequest("Claim value must be a valid game type or server ID.");
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authResource,
                AuthPolicies.Users_ManageClaims,
                nameof(CreateUserClaim),
                "UserClaim",
                $"ProfileId:{id},GameType:{gameTypeForTelemetry},ClaimType:{claimType}").ConfigureAwait(false);

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

                var definition = AdditionalPermission.GetDefinition(claimType);
                var displayName = definition?.DisplayName ?? claimType;
                this.AddAlertSuccess($"The '{displayName}' permission has been added to {user?.UserName ?? userProfileData.DisplayName}");

                TrackSuccessTelemetry("UserClaimCreated", nameof(CreateUserClaim), new Dictionary<string, string>
                {
                    { "ProfileId", id.ToString() },
                    { "ClaimType", claimType },
                    { "ClaimValue", claimValue },
                    { "GameType", gameTypeForTelemetry }
                });
            }
            else
            {
                var user = !string.IsNullOrEmpty(userProfileData.XtremeIdiotsForumId)
                    ? await userManager.FindByIdAsync(userProfileData.XtremeIdiotsForumId)
                    : null;

                var definition = AdditionalPermission.GetDefinition(claimType);
                var displayName = definition?.DisplayName ?? claimType;
                this.AddAlertSuccess($"Nothing to do - {user?.UserName ?? userProfileData.DisplayName} already has the '{displayName}' permission");
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

            var canDeleteUserClaim = false;

            if (Guid.TryParse(claim.ClaimValue, out var serverGuid))
            {
                // Server-scoped claim — look up the server for auth
                var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(serverGuid, cancellationToken).ConfigureAwait(false);

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
                        AuthPolicies.Users_ManageClaims,
                        nameof(RemoveUserClaim),
                        "UserClaim",
                        $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType}").ConfigureAwait(false);

                    if (authResult is not null)
                        return authResult;
                    canDeleteUserClaim = true;
                }
            }
            else if (Enum.TryParse<GameType>(claim.ClaimValue, out var gameType))
            {
                // Game-scoped claim — use the GameType directly for auth
                var authResult = await CheckAuthorizationAsync(
                    authorizationService,
                    gameType,
                    AuthPolicies.Users_ManageClaims,
                    nameof(RemoveUserClaim),
                    "UserClaim",
                    $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType}").ConfigureAwait(false);

                if (authResult is not null)
                    return authResult;
                canDeleteUserClaim = true;
            }
            else
            {
                // Unknown claim value format — treat as legacy, allow deletion
                Logger.LogInformation("Unrecognised claim value format for profile {ProfileId}, allowing deletion", id);
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

    /// <summary>
    /// Displays notification management for a specific user (admin view).
    /// Shows notification preferences and recent notification history.
    /// </summary>
    /// <param name="id">The user profile ID to manage notifications for</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The manage notifications view with preferences and history</returns>
    [HttpGet]
    public async Task<IActionResult> ManageNotifications(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                new object(),
                AuthPolicies.Users_Read,
                nameof(ManageNotifications),
                "UserNotifications").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfile(id, cancellationToken).ConfigureAwait(false);

            if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
            {
                Logger.LogWarning("User profile {ProfileId} not found when managing notifications", id);
                return NotFound();
            }

            var userProfile = userProfileResponse.Result.Data;

            var typesResponse = await repositoryApiClient.NotificationTypes.V1
                .GetNotificationTypes(cancellationToken).ConfigureAwait(false);

            var prefsResponse = await repositoryApiClient.NotificationPreferences.V1
                .GetNotificationPreferences(id, cancellationToken).ConfigureAwait(false);

            var notificationsResponse = await repositoryApiClient.Notifications.V1
                .GetNotifications(id, null, 0, 50, null, cancellationToken).ConfigureAwait(false);

            var vm = new ManageUserNotificationsViewModel
            {
                UserProfileId = userProfile.UserProfileId,
                DisplayName = userProfile.DisplayName ?? "Unknown",
                NotificationTypes = [.. (typesResponse.Result?.Data?.Items ?? []).Select(t => new NotificationTypeEntry
                {
                    NotificationTypeId = Guid.TryParse(t.NotificationTypeId, out var tid) ? tid : Guid.Empty,
                    Name = t.DisplayName,
                    Description = t.Description,
                    SupportsEmail = t.SupportsEmail,
                    SupportsInApp = t.SupportsInSite
                })],
                Preferences = [.. (prefsResponse.Result?.Data?.Items ?? []).Select(p => new NotificationPreferenceEntry
                {
                    NotificationTypeId = Guid.TryParse(p.NotificationTypeId, out var pid) ? pid : Guid.Empty,
                    EmailEnabled = p.EmailEnabled,
                    InAppEnabled = p.InSiteEnabled
                })],
                Notifications = [.. (notificationsResponse.Result?.Data?.Items ?? []).Select(n => new NotificationEntry
                {
                    NotificationId = n.NotificationId,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationTypeId,
                    SentAt = n.CreatedAt,
                    IsRead = n.IsRead,
                    EmailSent = n.EmailSent
                })]
            };

            return View(vm);
        }, nameof(ManageNotifications)).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves notification preferences for a specific user (admin action).
    /// Processes the submitted notification preference form and updates via the API.
    /// </summary>
    /// <param name="id">The user profile ID to update preferences for</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to ManageNotifications on success</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUserNotificationPreferences(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                new object(),
                AuthPolicies.Users_Read,
                nameof(UpdateUserNotificationPreferences),
                "UserNotifications").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfile(id, cancellationToken).ConfigureAwait(false);

            if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
            {
                Logger.LogWarning("User profile {ProfileId} not found when updating notification preferences", id);
                return NotFound();
            }

            // Build preferences from form data
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var typeIds = form.Keys
                .Where(k => k.StartsWith("insite_", StringComparison.Ordinal) || k.StartsWith("email_", StringComparison.Ordinal))
                .Select(k => k.Split('_', 2)[1])
                .Distinct()
                .ToList();

            var editDtos = new List<EditNotificationPreferenceDto>();
            foreach (var typeId in typeIds)
            {
                editDtos.Add(new EditNotificationPreferenceDto(typeId)
                {
                    InSiteEnabled = form.ContainsKey($"insite_{typeId}"),
                    EmailEnabled = form.ContainsKey($"email_{typeId}")
                });
            }

            // Handle types where both checkboxes are unchecked
            var allTypesResponse = await repositoryApiClient.NotificationTypes.V1
                .GetNotificationTypes(cancellationToken).ConfigureAwait(false);
            foreach (var t in allTypesResponse.Result?.Data?.Items ?? [])
            {
                if (!typeIds.Contains(t.NotificationTypeId))
                {
                    editDtos.Add(new EditNotificationPreferenceDto(t.NotificationTypeId)
                    {
                        InSiteEnabled = false,
                        EmailEnabled = false
                    });
                }
            }

            await repositoryApiClient.NotificationPreferences.V1
                .UpdateNotificationPreferences(id, editDtos, cancellationToken).ConfigureAwait(false);

            this.AddAlertSuccess($"Notification preferences for {userProfileResponse.Result.Data.DisplayName} have been updated");

            TrackSuccessTelemetry("UserNotificationPreferencesUpdated", nameof(UpdateUserNotificationPreferences), new Dictionary<string, string>
            {
                { "ProfileId", id.ToString() }
            });

            return RedirectToAction(nameof(ManageNotifications), new { id });
        }, nameof(UpdateUserNotificationPreferences), id.ToString());
    }
}