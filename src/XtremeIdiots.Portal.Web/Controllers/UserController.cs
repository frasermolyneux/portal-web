using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
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
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    private const int ManageProfileRecentNotificationLimit = 50;
    private const string NotificationPreferencesUnavailableMessage = "Notification preferences are currently unavailable. Please try again later.";
    private const string NotificationHistoryUnavailableMessage = "Notification history is currently unavailable. Please try again later.";
    private const string PermissionManagementUnavailableMessage = "Additional permission management is currently unavailable. Please try again later.";
    private const string NotificationPreferencesSaveFailedMessage = "Failed to update notification preferences. Please try again.";

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
    /// Displays moderator access for one game that the current user may administer.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> TeamAccess(GameType? gameType)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!gameType.HasValue)
            {
                return NotFound();
            }

            var normalizedGameType = GameTypeAuthorizationExtensions.NormalizeTeamAccessGameType(gameType.Value);
            if (!GameTypeAuthorizationExtensions.TeamAccessGameTypes.Contains(normalizedGameType))
            {
                return NotFound();
            }

            if (normalizedGameType != gameType.Value)
            {
                return RedirectToAction(nameof(TeamAccess), new { gameType = normalizedGameType });
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                normalizedGameType,
                AuthPolicies.Users_ManageClaims,
                nameof(TeamAccess),
                "GameTeamAccess",
                $"GameType:{normalizedGameType}").ConfigureAwait(false);

            if (authResult is not null)
            {
                return authResult;
            }

            var availableGameTypes = await authorizationService
                .GetAuthorizedTeamAccessGameTypesAsync(User, AuthPolicies.Users_ManageClaims)
                .ConfigureAwait(false);

            return View(new GameTeamAccessViewModel
            {
                GameType = normalizedGameType,
                AvailableGameTypes = availableGameTypes
            });
        }, nameof(TeamAccess)).ConfigureAwait(false);
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
    public async Task<IActionResult> ManageProfile(Guid id, string? tab = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (model, errorResult) = await BuildManageUserProfileViewModelAsync(
                id,
                NormalizeManageProfileTab(tab),
                postedPreferencesOverride: null,
                cancellationToken).ConfigureAwait(false);

            return errorResult ?? View(model);
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

            var safeRequestedTargetUserId = SanitizeForLog(id);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                new object(),
                AuthPolicies.Users_LogOut,
                nameof(LogUserOut),
                "User",
                $"TargetUserId:{safeRequestedTargetUserId}").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var user = await userManager.FindByIdAsync(id).ConfigureAwait(false);

            if (user is null)
            {
                Logger.LogWarning("Could not find user with ID '{UserId}' for force logout", id);
                this.AddAlertWarning($"Could not find user with XtremeIdiots ID '{id}', or there is no user logged in with that XtremeIdiots ID");
                return RedirectToAction(nameof(Index));
            }

            var targetUserProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(id, cancellationToken).ConfigureAwait(false);

            if (targetUserProfileResponse.Result?.Data?.UserProfileClaims is null ||
                string.IsNullOrWhiteSpace(targetUserProfileResponse.Result.Data.XtremeIdiotsForumId) ||
                !string.Equals(targetUserProfileResponse.Result.Data.XtremeIdiotsForumId, id, StringComparison.Ordinal))
            {
                var safeTargetUserId = SanitizeForLog(id);
                Logger.LogWarning("Could not authoritatively resolve target roles for logout of user {UserId}", safeTargetUserId);
                TrackUnauthorizedAccessAttempt(nameof(LogUserOut), "User", $"TargetUserId:{safeTargetUserId},Reason:TargetRolesUnavailable");
                return Unauthorized();
            }

            if (!BaseAuthorizationHelper.HasGlobalAdminClaim(User) &&
                HasProtectedLogoutRole(targetUserProfileResponse.Result.Data.UserProfileClaims))
            {
                var safeActorUserId = SanitizeForLog(User.XtremeIdiotsId());
                var safeTargetUserId = SanitizeForLog(id);
                Logger.LogWarning("User {ActorUserId} denied force logout against protected target {TargetUserId}",
                    safeActorUserId, safeTargetUserId);
                TrackUnauthorizedAccessAttempt(nameof(LogUserOut), "User", $"TargetUserId:{safeTargetUserId},Reason:ProtectedRoleTarget");
                return Unauthorized();
            }

            await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

            this.AddAlertSuccess($"User {user.UserName} has been force logged out (this may take up to 15 minutes)");

            TrackSuccessTelemetry("UserForceLoggedOut", nameof(LogUserOut), new Dictionary<string, string>
            {
                { "TargetUser", user.UserName ?? "" },
                { "TargetUserId", safeRequestedTargetUserId }
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
            var safeClaimType = SanitizeForLog(claimType);
            var safeClaimValue = SanitizeForLog(claimValue);

            if (!AdditionalPermission.IsAllowed(claimType))
            {
                Logger.LogWarning("Invalid claim type '{ClaimType}' attempted for profile {ProfileId}", safeClaimType, id);
                return BadRequest($"Invalid permission type: {claimType}");
            }

            if (string.IsNullOrWhiteSpace(claimValue))
            {
                Logger.LogWarning("Empty claim value for claim type '{ClaimType}' on profile {ProfileId}", safeClaimType, id);
                return BadRequest("A scope value must be provided.");
            }

            var definition = AdditionalPermission.GetDefinition(claimType);
            if (definition is null)
            {
                Logger.LogWarning("Permission definition missing for claim type '{ClaimType}' on profile {ProfileId}", safeClaimType, id);
                return BadRequest($"Invalid permission type: {claimType}");
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
                if (definition.Scope == PermissionScope.Game)
                {
                    Logger.LogWarning("Game-only claim type '{ClaimType}' was posted with server scope '{ClaimValue}' for profile {ProfileId}", safeClaimType, safeClaimValue, id);
                    return BadRequest("This permission must be scoped to a game type.");
                }

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
                if (definition.Scope == PermissionScope.Server)
                {
                    Logger.LogWarning("Server-only claim type '{ClaimType}' was posted with game scope '{ClaimValue}' for profile {ProfileId}", safeClaimType, safeClaimValue, id);
                    return BadRequest("This permission must be scoped to a server.");
                }

                // Game-scoped claim — use the GameType directly for auth
                authResource = gameType;
                gameTypeForTelemetry = gameType.ToString();
            }
            else
            {
                Logger.LogWarning("Invalid claim value '{ClaimValue}' — not a valid GameType or server GUID", safeClaimValue);
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

                var displayName = definition?.DisplayName ?? claimType;
                this.AddAlertSuccess($"Nothing to do - {user?.UserName ?? userProfileData.DisplayName} already has the '{displayName}' permission");
            }

            return RedirectToManageProfileTab(id, ManageUserProfileViewModel.PermissionsTabName);
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

            if (claim.SystemGenerated)
            {
                Logger.LogWarning("Attempt to remove system-generated claim {ClaimId} from user profile {ProfileId}", claimId, id);
                TrackUnauthorizedAccessAttempt(nameof(RemoveUserClaim), "UserClaim",
                    $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType},Reason:SystemGenerated");
                return Unauthorized();
            }

            if (Guid.TryParse(claim.ClaimValue, out var serverGuid))
            {
                // Server-scoped claim — look up the server for auth
                var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(serverGuid, cancellationToken).ConfigureAwait(false);
                var gameServer = gameServerApiResponse.Result?.Data;

                if (gameServerApiResponse.IsNotFound)
                {
                    if (!BaseAuthorizationHelper.HasGlobalAdminClaim(User))
                    {
                        Logger.LogWarning("Head-admin-scoped cleanup denied for deleted server claim {ClaimId} on profile {ProfileId}", claimId, id);
                        TrackUnauthorizedAccessAttempt(nameof(RemoveUserClaim), "UserClaim",
                            $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType},Reason:DeletedServerScope");
                        return Unauthorized();
                    }
                }
                else
                {
                    if (gameServer is null)
                    {
                        Logger.LogWarning("Game server data is null for claim {ClaimId} on profile {ProfileId}", claimId, id);
                        return BadRequest();
                    }

                    var authResult = await CheckAuthorizationAsync(
                        authorizationService,
                        gameServer.GameType,
                        AuthPolicies.Users_ManageClaims,
                        nameof(RemoveUserClaim),
                        "UserClaim",
                        $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType}").ConfigureAwait(false);

                    if (authResult is not null)
                        return authResult;
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
            }
            else if (!BaseAuthorizationHelper.HasGlobalAdminClaim(User))
            {
                Logger.LogWarning("Head-admin-scoped cleanup denied for unrecognised claim value on claim {ClaimId} for profile {ProfileId}", claimId, id);
                TrackUnauthorizedAccessAttempt(nameof(RemoveUserClaim), "UserClaim",
                    $"ProfileId:{id},ClaimId:{claimId},ClaimType:{claim.ClaimType},Reason:UnknownScopeValue");
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

            return RedirectToManageProfileTab(id, ManageUserProfileViewModel.PermissionsTabName);
        }, nameof(RemoveUserClaim), id.ToString());
    }

    /// <summary>
    /// Redirects legacy notification management routes to the consolidated manage profile experience.
    /// </summary>
    /// <param name="id">The user profile ID to manage notifications for</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects to ManageProfile with the notifications tab route value.</returns>
    [HttpGet]
    public async Task<IActionResult> ManageNotifications(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var redirect = RedirectToManageProfileNotifications(id);

        return await ExecuteWithErrorHandlingAsync(
            () => Task.FromResult(redirect),
            nameof(ManageNotifications)).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves notification preferences for a specific user (admin action).
    /// Processes the submitted notification preference form and updates via the API.
    /// </summary>
    /// <param name="model">The typed preference update payload.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects back to ManageProfile on success or returns the manage profile view on validation failure.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUserNotificationPreferences(
        ManageUserNotificationPreferencesUpdateModel model,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                new object(),
                AuthPolicies.Users_ManageNotificationPreferences,
                nameof(UpdateUserNotificationPreferences),
                "UserNotifications").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfile(model.Id, cancellationToken).ConfigureAwait(false);

            if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
            {
                Logger.LogWarning("User profile {ProfileId} not found when updating notification preferences", model.Id);
                return NotFound();
            }

            var allTypesResponse = await repositoryApiClient.NotificationTypes.V1
                .GetNotificationTypes(cancellationToken).ConfigureAwait(false);

            if (!allTypesResponse.IsSuccess || allTypesResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Invalid notification type response when updating notification preferences for profile {ProfileId}", model.Id);
                return BadRequest();
            }

            var explicitPreferencesResponse = await repositoryApiClient.NotificationPreferences.V1
                .GetNotificationPreferences(model.Id, cancellationToken).ConfigureAwait(false);

            if (!explicitPreferencesResponse.IsSuccess || explicitPreferencesResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Invalid notification preference response when updating notification preferences for profile {ProfileId}", model.Id);
                return BadRequest();
            }

            var notificationTypes = allTypesResponse.Result.Data.Items
                .Select(MapNotificationType)
                .ToList();
            NormalizePostedNotificationPreferenceBooleans(model, Request.HasFormContentType ? Request.Form : null);
            var notificationTypeLookup = notificationTypes.ToDictionary(
                notificationType => notificationType.NotificationTypeId,
                StringComparer.OrdinalIgnoreCase);

            var postedPreferencesByType = new Dictionary<string, ManageUserNotificationPreferenceUpdateEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var postedPreference in model.Preferences)
            {
                if (string.IsNullOrWhiteSpace(postedPreference.NotificationTypeId))
                {
                    ModelState.AddModelError(nameof(model.Preferences), "Each notification preference entry must include a notification type.");
                    continue;
                }

                if (!postedPreferencesByType.TryAdd(postedPreference.NotificationTypeId, postedPreference))
                {
                    ModelState.AddModelError(nameof(model.Preferences), $"Duplicate notification preference entry '{SanitizeForLog(postedPreference.NotificationTypeId)}' was submitted.");
                }
            }

            foreach (var postedNotificationTypeId in postedPreferencesByType.Keys)
            {
                if (!notificationTypeLookup.ContainsKey(postedNotificationTypeId))
                {
                    ModelState.AddModelError(nameof(model.Preferences), $"Unknown notification type '{SanitizeForLog(postedNotificationTypeId)}' was submitted.");
                }
            }

            foreach (var notificationType in notificationTypes)
            {
                if (!postedPreferencesByType.TryGetValue(notificationType.NotificationTypeId, out var postedPreference))
                {
                    ModelState.AddModelError(nameof(model.Preferences), "Notification preference submission is incomplete. Refresh the page and try again.");
                    continue;
                }

                if (postedPreference.InAppEnabled && !notificationType.SupportsInSite)
                {
                    ModelState.AddModelError(nameof(model.Preferences), $"Notification type '{SanitizeForLog(notificationType.DisplayName)}' does not support in-app delivery.");
                }

                if (postedPreference.EmailEnabled && !notificationType.SupportsEmail)
                {
                    ModelState.AddModelError(nameof(model.Preferences), $"Notification type '{SanitizeForLog(notificationType.DisplayName)}' does not support email delivery.");
                }
            }

            if (!ModelState.IsValid)
            {
                var (invalidModel, errorResult) = await BuildManageUserProfileViewModelAsync(
                    model.Id,
                    ManageUserProfileViewModel.NotificationsTabName,
                    postedPreferencesByType,
                    cancellationToken).ConfigureAwait(false);

                return errorResult ?? View(nameof(ManageProfile), invalidModel);
            }

            var currentEffectivePreferences = BuildEffectiveNotificationPreferences(
                notificationTypes,
                [.. explicitPreferencesResponse.Result.Data.Items]);
            var explicitPreferences = explicitPreferencesResponse.Result.Data.Items.ToList();

            var editDtos = BuildNotificationPreferenceUpdateDtos(
                notificationTypes,
                currentEffectivePreferences,
                explicitPreferences,
                postedPreferencesByType);
            var changedPreferences = BuildNotificationPreferenceChangeContext(currentEffectivePreferences, editDtos);

            if (changedPreferences == "None")
            {
                this.AddAlertInfo("Notification preferences are already up to date.");
                return RedirectToManageProfileNotifications(model.Id);
            }

            var updateResult = await repositoryApiClient.NotificationPreferences.V1
                .UpdateNotificationPreferences(model.Id, editDtos, cancellationToken).ConfigureAwait(false);

            if (!updateResult.IsSuccess)
            {
                Logger.LogWarning("Failed to update notification preferences for profile {ProfileId}", model.Id);
                this.AddAlertDanger(NotificationPreferencesSaveFailedMessage);

                var (failedModel, errorResult) = await BuildManageUserProfileViewModelAsync(
                    model.Id,
                    ManageUserProfileViewModel.NotificationsTabName,
                    postedPreferencesByType,
                    cancellationToken).ConfigureAwait(false);

                return errorResult ?? View(nameof(ManageProfile), failedModel);
            }

            this.AddAlertSuccess($"Notification preferences for {WebUtility.HtmlEncode(userProfileResponse.Result.Data.DisplayName)} have been updated");

            TrackSuccessTelemetry("UserNotificationPreferencesUpdated", nameof(UpdateUserNotificationPreferences), new Dictionary<string, string>
            {
                { "ProfileId", model.Id.ToString() },
                { "TargetUser", SanitizeForLog(userProfileResponse.Result.Data.DisplayName) },
                { "ChangedPreferences", SanitizeForLog(changedPreferences) }
            });

            return RedirectToManageProfileNotifications(model.Id);
        }, nameof(UpdateUserNotificationPreferences), model.Id.ToString());
    }

    private IActionResult RedirectToManageProfileNotifications(Guid id)
    {
        return RedirectToManageProfileTab(id, ManageUserProfileViewModel.NotificationsTabName);
    }

    private IActionResult RedirectToManageProfileTab(Guid id, string tab)
    {
        if (Url is null)
        {
            return RedirectToAction(nameof(ManageProfile), new { id, tab });
        }

        var manageProfileUrl = Url.Action(nameof(ManageProfile), new
        {
            id,
            tab
        });

        return string.IsNullOrWhiteSpace(manageProfileUrl)
            ? RedirectToAction(nameof(ManageProfile), new { id, tab })
            : Redirect($"{manageProfileUrl}#{tab}");
    }

    private async Task<(ManageUserProfileViewModel? Model, IActionResult? ErrorResult)> BuildManageUserProfileViewModelAsync(
        Guid id,
        string activeTab,
        IReadOnlyDictionary<string, ManageUserNotificationPreferenceUpdateEntry>? postedPreferencesOverride,
        CancellationToken cancellationToken)
    {
        string[] requiredClaims = [UserProfileClaimType.Webmaster, UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin];
        var (gameTypes, gameServerIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);
        var assignableGameTypes = User.GetGameTypesForGameServers();
        var userProfileDtoApiResponse = await repositoryApiClient.UserProfiles.V1.GetUserProfile(id, cancellationToken).ConfigureAwait(false);

        if (userProfileDtoApiResponse.IsNotFound)
        {
            Logger.LogWarning("User profile {ProfileId} not found when managing profile", id);
            return (null, NotFound());
        }

        if (userProfileDtoApiResponse.Result?.Data is null)
        {
            Logger.LogWarning("Invalid API response when managing profile {ProfileId}", id);
            return (null, BadRequest());
        }

        var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
            gameTypes, gameServerIds, null, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

        string? permissionsErrorMessage = null;
        List<GameServerDto> visibleGameServers = [];
        List<GameType> assignableGameTypesForView = [.. assignableGameTypes];

        if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Assignable game server list unavailable when managing profile {ProfileId}", id);
            permissionsErrorMessage = PermissionManagementUnavailableMessage;
            assignableGameTypesForView = [];
        }
        else
        {
            visibleGameServers = [.. gameServersApiResponse.Result.Data.Items];
        }

        var (notificationTypes, notificationPreferences, recentNotifications, notificationPreferencesErrorMessage, notificationHistoryErrorMessage) =
            await BuildManageUserNotificationDataAsync(id, cancellationToken).ConfigureAwait(false);

        ViewData["AssignableGameServersSelect"] = new SelectList(
            visibleGameServers.Where(server => assignableGameTypes.Contains(server.GameType)),
            "GameServerId",
            "Title");

        var profileData = userProfileDtoApiResponse.Result.Data;
        var identitySummary = await BuildIdentitySummaryAsync(profileData).ConfigureAwait(false);
        var canUpdateNotificationPreferences = (await authorizationService.AuthorizeAsync(
            User,
            new object(),
            AuthPolicies.Users_ManageNotificationPreferences).ConfigureAwait(false)).Succeeded;

        var notificationPreferenceEntries = ApplyNotificationPreferenceOverrides(
            notificationPreferences,
            postedPreferencesOverride);

        return (new ManageUserProfileViewModel
        {
            Profile = profileData,
            Identity = identitySummary,
            AssignableGameTypes = assignableGameTypesForView,
            Claims = await BuildManageUserProfileClaimEntriesAsync(
                profileData,
                visibleGameServers,
                allowMutationAffordances: permissionsErrorMessage is null,
                cancellationToken).ConfigureAwait(false),
            NotificationTypes = notificationTypes,
            NotificationPreferences = notificationPreferenceEntries,
            RecentNotifications = recentNotifications,
            CanUpdateNotificationPreferences = canUpdateNotificationPreferences,
            ActiveTab = activeTab,
            NotificationPreferencesErrorMessage = notificationPreferencesErrorMessage,
            NotificationHistoryErrorMessage = notificationHistoryErrorMessage,
            PermissionsErrorMessage = permissionsErrorMessage
        }, null);
    }

    private async Task<IdentityUserSummary?> BuildIdentitySummaryAsync(UserProfileDto profileData)
    {
        IdentityUser? identityUser = null;
        if (profileData.XtremeIdiotsForumId is not null)
        {
            identityUser = await userManager.FindByIdAsync(profileData.XtremeIdiotsForumId.ToString()).ConfigureAwait(false);
        }

        identityUser ??= await userManager.FindByIdAsync(profileData.UserProfileId.ToString()).ConfigureAwait(false);

        return identityUser is null
            ? null
            : new IdentityUserSummary
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
    }

    private async Task<(
        List<NotificationTypeViewModel> NotificationTypes,
        List<ManageUserNotificationPreferenceEntry> NotificationPreferences,
        List<ManageUserNotificationHistoryEntry> RecentNotifications,
        string? NotificationPreferencesErrorMessage,
        string? NotificationHistoryErrorMessage)> BuildManageUserNotificationDataAsync(
            Guid userProfileId,
            CancellationToken cancellationToken)
    {
        var notificationTypes = new List<NotificationTypeViewModel>();
        var notificationTypeLookup = new Dictionary<string, NotificationTypeViewModel>(StringComparer.OrdinalIgnoreCase);
        var notificationPreferences = new List<ManageUserNotificationPreferenceEntry>();
        var recentNotifications = new List<ManageUserNotificationHistoryEntry>();
        string? notificationPreferencesErrorMessage = null;
        string? notificationHistoryErrorMessage = null;

        var typesResponse = await repositoryApiClient.NotificationTypes.V1
            .GetNotificationTypes(cancellationToken).ConfigureAwait(false);

        if (!typesResponse.IsSuccess || typesResponse.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Invalid notification type response when managing profile {ProfileId}", userProfileId);
            notificationPreferencesErrorMessage = NotificationPreferencesUnavailableMessage;
        }
        else
        {
            notificationTypes =
            [
                .. typesResponse.Result.Data.Items.Select(MapNotificationType)
            ];
            notificationTypeLookup = notificationTypes.ToDictionary(
                notificationType => notificationType.NotificationTypeId,
                StringComparer.OrdinalIgnoreCase);

            var preferencesResponse = await repositoryApiClient.NotificationPreferences.V1
                .GetNotificationPreferences(userProfileId, cancellationToken).ConfigureAwait(false);

            if (!preferencesResponse.IsSuccess || preferencesResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Invalid notification preference response when managing profile {ProfileId}", userProfileId);
                notificationPreferencesErrorMessage = NotificationPreferencesUnavailableMessage;
            }
            else
            {
                notificationPreferences = BuildEffectiveNotificationPreferences(
                    notificationTypes,
                    [.. preferencesResponse.Result.Data.Items]);
            }
        }

        var notificationsResponse = await repositoryApiClient.Notifications.V1
            .GetNotifications(userProfileId, null, 0, ManageProfileRecentNotificationLimit, NotificationOrder.CreatedAtDesc, cancellationToken)
            .ConfigureAwait(false);

        if (!notificationsResponse.IsSuccess || notificationsResponse.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Invalid notification history response when managing profile {ProfileId}", userProfileId);
            notificationHistoryErrorMessage = NotificationHistoryUnavailableMessage;
        }
        else
        {
            recentNotifications =
            [
                .. notificationsResponse.Result.Data.Items.Select(
                    notification => MapNotificationHistory(notification, notificationTypeLookup))
            ];
        }

        return (notificationTypes, notificationPreferences, recentNotifications, notificationPreferencesErrorMessage, notificationHistoryErrorMessage);
    }

    private static NotificationTypeViewModel MapNotificationType(NotificationTypeDto notificationType)
    {
        return new NotificationTypeViewModel(
            notificationType.NotificationTypeId,
            notificationType.DisplayName,
            notificationType.Description,
            notificationType.SupportsInSite,
            notificationType.SupportsEmail,
            SupportsDefaultChannel(notificationType.DefaultChannels, "InSite"),
            SupportsDefaultChannel(notificationType.DefaultChannels, "Email"));
    }

    private static List<ManageUserNotificationPreferenceEntry> BuildEffectiveNotificationPreferences(
        IReadOnlyCollection<NotificationTypeViewModel> notificationTypes,
        IReadOnlyCollection<NotificationPreferenceDto> explicitPreferences)
    {
        var explicitPreferencesByType = explicitPreferences
            .GroupBy(preference => preference.NotificationTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return [.. notificationTypes.Select(notificationType =>
        {
            explicitPreferencesByType.TryGetValue(notificationType.NotificationTypeId, out var explicitPreference);

            return new ManageUserNotificationPreferenceEntry
            {
                NotificationTypeId = notificationType.NotificationTypeId,
                InAppEnabled = explicitPreference?.InSiteEnabled ?? notificationType.DefaultInSiteEnabled,
                EmailEnabled = explicitPreference?.EmailEnabled ?? notificationType.DefaultEmailEnabled,
                ExplicitInAppEnabled = explicitPreference?.InSiteEnabled,
                ExplicitEmailEnabled = explicitPreference?.EmailEnabled
            };
        })];
    }

    private static List<ManageUserNotificationPreferenceEntry> ApplyNotificationPreferenceOverrides(
        List<ManageUserNotificationPreferenceEntry> notificationPreferences,
        IReadOnlyDictionary<string, ManageUserNotificationPreferenceUpdateEntry>? postedPreferencesOverride)
    {
        if (postedPreferencesOverride is null || postedPreferencesOverride.Count == 0)
        {
            return notificationPreferences;
        }

        foreach (var notificationPreference in notificationPreferences)
        {
            if (postedPreferencesOverride.TryGetValue(notificationPreference.NotificationTypeId, out var postedPreference))
            {
                notificationPreference.InAppEnabled = postedPreference.InAppEnabled;
                notificationPreference.EmailEnabled = postedPreference.EmailEnabled;
            }
        }

        return notificationPreferences;
    }

    private static List<EditNotificationPreferenceDto> BuildNotificationPreferenceUpdateDtos(
        IReadOnlyCollection<NotificationTypeViewModel> notificationTypes,
        IReadOnlyCollection<ManageUserNotificationPreferenceEntry> currentEffectivePreferences,
        IReadOnlyCollection<NotificationPreferenceDto> explicitPreferences,
        Dictionary<string, ManageUserNotificationPreferenceUpdateEntry> postedPreferencesByType)
    {
        var currentEffectiveByType = currentEffectivePreferences.ToDictionary(
            preference => preference.NotificationTypeId,
            StringComparer.OrdinalIgnoreCase);
        var explicitPreferencesByType = explicitPreferences
            .GroupBy(preference => preference.NotificationTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var updateDtos = new List<EditNotificationPreferenceDto>();

        foreach (var notificationType in notificationTypes)
        {
            var postedPreference = postedPreferencesByType[notificationType.NotificationTypeId];
            var desiredInAppEnabled = notificationType.SupportsInSite && postedPreference.InAppEnabled;
            var desiredEmailEnabled = notificationType.SupportsEmail && postedPreference.EmailEnabled;

            if (explicitPreferencesByType.ContainsKey(notificationType.NotificationTypeId))
            {
                updateDtos.Add(new EditNotificationPreferenceDto(notificationType.NotificationTypeId)
                {
                    InSiteEnabled = desiredInAppEnabled,
                    EmailEnabled = desiredEmailEnabled
                });

                continue;
            }

            var currentEffectivePreference = currentEffectiveByType[notificationType.NotificationTypeId];
            if (currentEffectivePreference.InAppEnabled == desiredInAppEnabled &&
                currentEffectivePreference.EmailEnabled == desiredEmailEnabled)
            {
                continue;
            }

            updateDtos.Add(new EditNotificationPreferenceDto(notificationType.NotificationTypeId)
            {
                InSiteEnabled = desiredInAppEnabled,
                EmailEnabled = desiredEmailEnabled
            });
        }

        return updateDtos;
    }

    private static void NormalizePostedNotificationPreferenceBooleans(
        ManageUserNotificationPreferencesUpdateModel model,
        IFormCollection? form)
    {
        if (form is null)
        {
            return;
        }

        for (var index = 0; index < model.Preferences.Count; index++)
        {
            var preference = model.Preferences[index];
            preference.EmailEnabled = GetPostedCheckboxValue(form, $"Preferences[{index}].EmailEnabled");
            preference.InAppEnabled = GetPostedCheckboxValue(form, $"Preferences[{index}].InAppEnabled");
        }
    }

    private static bool GetPostedCheckboxValue(IFormCollection form, string key)
    {
        if (!form.TryGetValue(key, out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ManageUserNotificationHistoryEntry MapNotificationHistory(
        NotificationDto notification,
        Dictionary<string, NotificationTypeViewModel> notificationTypeLookup)
    {
        return new ManageUserNotificationHistoryEntry
        {
            NotificationId = notification.NotificationId,
            Title = notification.Title,
            Message = notification.Message,
            NotificationType = notificationTypeLookup.TryGetValue(notification.NotificationTypeId, out var notificationType)
                ? notificationType.DisplayName
                : notification.NotificationTypeId,
            SentAt = notification.CreatedAt,
            IsRead = notification.IsRead,
            EmailSent = notification.EmailSent
        };
    }

    private static bool SupportsDefaultChannel(string? defaultChannels, string channelName)
    {
        return !string.IsNullOrWhiteSpace(defaultChannels) &&
               defaultChannels.Contains(channelName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNotificationPreferenceChangeContext(
        IReadOnlyCollection<ManageUserNotificationPreferenceEntry> currentPreferences,
        IReadOnlyCollection<EditNotificationPreferenceDto> updatedPreferences)
    {
        var currentPreferencesByType = currentPreferences.ToDictionary(
            preference => preference.NotificationTypeId,
            StringComparer.OrdinalIgnoreCase);
        var changes = new List<string>();

        foreach (var updatedPreference in updatedPreferences)
        {
            if (!currentPreferencesByType.TryGetValue(updatedPreference.NotificationTypeId, out var currentPreference))
            {
                continue;
            }

            if (currentPreference.InAppEnabled == updatedPreference.InSiteEnabled &&
                currentPreference.EmailEnabled == updatedPreference.EmailEnabled)
            {
                continue;
            }

            changes.Add($"{updatedPreference.NotificationTypeId}:InApp={updatedPreference.InSiteEnabled},Email={updatedPreference.EmailEnabled}");
        }

        return changes.Count == 0
            ? "None"
            : string.Join(";", changes);
    }

    private static string NormalizeManageProfileTab(string? tab)
    {
        if (string.Equals(tab, ManageUserProfileViewModel.PermissionsTabName, StringComparison.OrdinalIgnoreCase))
        {
            return ManageUserProfileViewModel.PermissionsTabName;
        }

        return string.Equals(tab, ManageUserProfileViewModel.NotificationsTabName, StringComparison.OrdinalIgnoreCase)
            ? ManageUserProfileViewModel.NotificationsTabName
            : ManageUserProfileViewModel.OverviewTabName;
    }

    private static bool HasProtectedLogoutRole(IEnumerable<UserProfileClaimDto> claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.ClaimType, claim.ClaimValue)),
            authenticationType: "TargetProfileClaims"));

        return BaseAuthorizationHelper.HasGlobalAdminClaim(principal);
    }

    private async Task<List<ManageUserProfileClaimEntry>> BuildManageUserProfileClaimEntriesAsync(
        UserProfileDto profile,
        IReadOnlyCollection<GameServerDto> visibleGameServers,
        bool allowMutationAffordances,
        CancellationToken cancellationToken)
    {
        var isGlobalAdmin = BaseAuthorizationHelper.HasGlobalAdminClaim(User);
        var serverCache = visibleGameServers.ToDictionary(server => server.GameServerId, server => (GameServerDto?)server);
        var claimEntries = new List<ManageUserProfileClaimEntry>(profile.UserProfileClaims.Count);

        foreach (var claim in profile.UserProfileClaims)
        {
            claimEntries.Add(await BuildManageUserProfileClaimEntryAsync(
                claim,
                isGlobalAdmin,
                serverCache,
                allowMutationAffordances,
                cancellationToken).ConfigureAwait(false));
        }

        return claimEntries;
    }

    private async Task<ManageUserProfileClaimEntry> BuildManageUserProfileClaimEntryAsync(
        UserProfileClaimDto claim,
        bool isGlobalAdmin,
        IDictionary<Guid, GameServerDto?> serverCache,
        bool allowMutationAffordances,
        CancellationToken cancellationToken)
    {
        var definition = AdditionalPermission.GetDefinition(claim.ClaimType);
        var server = await ResolveGameServerForClaimAsync(claim.ClaimValue, serverCache, cancellationToken).ConfigureAwait(false);
        var canRemove = false;

        if (allowMutationAffordances && !claim.SystemGenerated)
        {
            if (isGlobalAdmin)
            {
                canRemove = true;
            }
            else if (server is not null)
            {
                canRemove = (await authorizationService.AuthorizeAsync(
                    User,
                    server.GameType,
                    AuthPolicies.Users_ManageClaims).ConfigureAwait(false)).Succeeded;
            }
            else if (Enum.TryParse<GameType>(claim.ClaimValue, out var gameType))
            {
                canRemove = (await authorizationService.AuthorizeAsync(
                    User,
                    gameType,
                    AuthPolicies.Users_ManageClaims).ConfigureAwait(false)).Succeeded;
            }
        }

        return new ManageUserProfileClaimEntry
        {
            UserProfileClaimId = claim.UserProfileClaimId,
            DisplayName = definition?.DisplayName ?? claim.ClaimType,
            ScopeDisplayValue = server?.Title ?? claim.ClaimValue,
            SystemGenerated = claim.SystemGenerated,
            CanRemove = canRemove
        };
    }

    private async Task<GameServerDto?> ResolveGameServerForClaimAsync(
        string claimValue,
        IDictionary<Guid, GameServerDto?> serverCache,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(claimValue, out var gameServerId))
        {
            return null;
        }

        if (serverCache.TryGetValue(gameServerId, out var cachedServer))
        {
            return cachedServer;
        }

        var gameServerResponse = await repositoryApiClient.GameServers.V1
            .GetGameServer(gameServerId, cancellationToken).ConfigureAwait(false);
        var resolvedServer = gameServerResponse.Result?.Data;

        serverCache[gameServerId] = resolvedServer;
        return resolvedServer;
    }

    private static string SanitizeForLog(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
