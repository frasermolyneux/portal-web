using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Handles user profile management operations including notification preferences
/// </summary>
/// <remarks>
/// Initializes a new instance of the ProfileController
/// </remarks>
/// <param name="repositoryApiClient">Client for repository API operations</param>
/// <param name="telemetryClient">Application Insights telemetry client</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize]
public class ProfileController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<ProfileController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    /// <summary>
    /// Displays the user profile management page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The profile management view</returns>
    [HttpGet]
    public async Task<IActionResult> Manage(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var model = await BuildProfileManageViewModel(cancellationToken).ConfigureAwait(false);
            return View(model);
        }, nameof(Manage)).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new active connected-player activation code for the current profile.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateConnectedPlayerCode(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfile = await GetCurrentUserProfile(cancellationToken).ConfigureAwait(false);
            if (userProfile is null)
            {
                this.AddAlertDanger("Unable to resolve your profile. Please sign in again and retry.");
                return RedirectToAction(nameof(Manage));
            }

            var activationResponse = await repositoryApiClient.ConnectedPlayers.V1
                .ActivateConnectedPlayerActivationCode(new ActivateConnectedPlayerActivationCodeDto
                {
                    UserProfileId = userProfile.UserProfileId
                }, cancellationToken)
                .ConfigureAwait(false);

            var activationCode = activationResponse.Result?.Data?.Code;
            if (activationResponse.IsSuccess && !string.IsNullOrWhiteSpace(activationCode))
            {
                this.AddAlertSuccess($"Activation code generated: {activationCode}");
            }
            else
            {
                this.AddAlertDanger("Failed to generate activation code. Please try again.");
            }

            return RedirectToAction(nameof(Manage));
        }, nameof(ActivateConnectedPlayerCode)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the notification preferences page where users can configure delivery channels
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The notification preferences view</returns>
    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var xtremeIdiotsId = User.XtremeIdiotsId();
            if (string.IsNullOrEmpty(xtremeIdiotsId))
                return RedirectToAction(nameof(Manage));

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(xtremeIdiotsId).ConfigureAwait(false);

            if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
                return RedirectToAction(nameof(Manage));

            var userProfile = userProfileResponse.Result.Data;

            var typesResponse = await repositoryApiClient.NotificationTypes.V1
                .GetNotificationTypes(cancellationToken).ConfigureAwait(false);

            var prefsResponse = await repositoryApiClient.NotificationPreferences.V1
                .GetNotificationPreferences(userProfile.UserProfileId, cancellationToken).ConfigureAwait(false);

            var notificationTypes = (typesResponse.Result?.Data?.Items ?? [])
                .Select(t => new NotificationTypeViewModel(
                    t.NotificationTypeId, t.DisplayName, t.Description,
                    t.SupportsInSite, t.SupportsEmail,
                    (t.DefaultChannels ?? "").Contains("InSite"),
                    (t.DefaultChannels ?? "").Contains("Email")))
                .ToList();

            var preferences = (prefsResponse.Result?.Data?.Items ?? [])
                .Select(p => new NotificationPreferenceViewModel(
                    p.NotificationTypeId, p.InSiteEnabled, p.EmailEnabled))
                .ToList();

            var model = new NotificationPreferencesPageViewModel(
                userProfile.UserProfileId,
                notificationTypes,
                preferences);

            return View(model);
        }, nameof(Notifications)).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves notification preferences submitted from the preferences form
    /// </summary>
    /// <param name="userProfileId">The user profile ID to save preferences for</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Redirects back to the notification preferences page</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Notifications(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            var typeIds = form.Keys
                .Where(k => k.StartsWith("insite_", StringComparison.Ordinal) || k.StartsWith("email_", StringComparison.Ordinal))
                .Select(k => k.Split('_', 2)[1])
                .Distinct()
                .ToList();

            var preferences = new List<NotificationPreferenceViewModel>();

            foreach (var typeId in typeIds)
            {
                preferences.Add(new NotificationPreferenceViewModel(
                    typeId,
                    InSiteEnabled: form.ContainsKey($"insite_{typeId}"),
                    EmailEnabled: form.ContainsKey($"email_{typeId}")));
            }

            // Handle notification types where both checkboxes are unchecked (not present in form)
            var allTypesResponse = await repositoryApiClient.NotificationTypes.V1
                .GetNotificationTypes(cancellationToken).ConfigureAwait(false);
            var allTypeIds = (allTypesResponse.Result?.Data?.Items ?? []).Select(t => t.NotificationTypeId);

            foreach (var typeId in allTypeIds)
            {
                if (!typeIds.Contains(typeId))
                {
                    preferences.Add(new NotificationPreferenceViewModel(
                        typeId,
                        InSiteEnabled: false,
                        EmailEnabled: false));
                }
            }

            var editDtos = preferences.Select(p => new EditNotificationPreferenceDto(p.NotificationTypeId)
            {
                InSiteEnabled = p.InSiteEnabled,
                EmailEnabled = p.EmailEnabled
            }).ToList();

            await repositoryApiClient.NotificationPreferences.V1
                .UpdateNotificationPreferences(userProfileId, editDtos, cancellationToken).ConfigureAwait(false);

            this.AddAlertSuccess("Notification preferences saved successfully.");
            TrackSuccessTelemetry("UserNotificationPreferencesUpdated", nameof(Notifications));
            return RedirectToAction(nameof(Notifications));
        }, nameof(Notifications)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays all notifications for the current user with pagination
    /// </summary>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The all notifications view</returns>
    [HttpGet]
    public async Task<IActionResult> AllNotifications(int page = 1, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var xtremeIdiotsId = User.XtremeIdiotsId();
            if (string.IsNullOrEmpty(xtremeIdiotsId))
                return RedirectToAction(nameof(Manage));

            var userProfileResponse = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(xtremeIdiotsId).ConfigureAwait(false);

            if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
                return RedirectToAction(nameof(Manage));

            const int pageSize = 20;
            var skipEntries = (Math.Max(1, page) - 1) * pageSize;
            var userProfileId = userProfileResponse.Result.Data.UserProfileId;

            var notificationsResponse = await repositoryApiClient.Notifications.V1
                .GetNotifications(userProfileId, null, skipEntries, pageSize, null, cancellationToken).ConfigureAwait(false);

            var unreadCountResponse = await repositoryApiClient.Notifications.V1
                .GetUnreadNotificationCount(userProfileId, cancellationToken).ConfigureAwait(false);

            var items = notificationsResponse.Result?.Data?.Items ?? [];
            var totalCount = notificationsResponse.Result?.Pagination?.TotalCount ?? 0;
            var unreadCount = unreadCountResponse.Result?.Data ?? 0;
            var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));

            var notifications = items.Select(n => new NotificationViewModel(
                n.NotificationId, n.Title, n.Message,
                "fa-solid fa-bell",
                n.CreatedAt, n.IsRead,
                n.ActionUrl)).ToList();

            var model = new AllNotificationsPageViewModel(
                notifications,
                CurrentPage: Math.Max(1, page),
                TotalPages: totalPages,
                TotalCount: totalCount,
                UnreadCount: unreadCount);

            return View(model);
        }, nameof(AllNotifications)).ConfigureAwait(false);
    }

    private async Task<ProfileManageViewModel> BuildProfileManageViewModel(CancellationToken cancellationToken)
    {
        var userProfile = await GetCurrentUserProfile(cancellationToken).ConfigureAwait(false);
        if (userProfile is null)
            return new ProfileManageViewModel(null, null, [], 0, false);

        const int linkedPlayersPageSize = 100;

        var activeCodeResult = await repositoryApiClient.ConnectedPlayers.V1
            .GetActiveConnectedPlayerActivationCode(userProfile.UserProfileId, cancellationToken)
            .ConfigureAwait(false);

        var linkedPlayersResult = await repositoryApiClient.ConnectedPlayers.V1
            .GetConnectedPlayersByUserProfile(userProfile.UserProfileId, 0, linkedPlayersPageSize, cancellationToken)
            .ConfigureAwait(false);

        var linkedPlayers = (linkedPlayersResult.Result?.Data?.Items ?? [])
            .Select(x => new ProfileLinkedPlayerViewModel(
                x.GameType.ToString(),
                x.Username,
                x.LinkMethod.ToString(),
                x.LinkedAtUtc,
                x.UnlinkedAtUtc,
                x.IsActive))
            .ToList();

        var activeCodeData = activeCodeResult.IsNotFound ? null : activeCodeResult.Result?.Data;
        var activeCode = activeCodeData is null
            ? null
            : new ProfileActivationCodeViewModel(
                activeCodeData.Code,
                activeCodeData.ExpiresAtUtc,
                activeCodeData.IsActive);

        var totalLinkedPlayers = linkedPlayersResult.Result?.Pagination?.TotalCount ?? linkedPlayers.Count;
        var isLinkedPlayersCapped = totalLinkedPlayers > linkedPlayers.Count;

        return new ProfileManageViewModel(
            userProfile.UserProfileId,
            activeCode,
            linkedPlayers,
            totalLinkedPlayers,
            isLinkedPlayersCapped);
    }

    private async Task<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles.UserProfileDto?> GetCurrentUserProfile(CancellationToken cancellationToken)
    {
        var xtremeIdiotsId = User.XtremeIdiotsId();
        if (string.IsNullOrEmpty(xtremeIdiotsId))
            return null;

        var userProfileResponse = await repositoryApiClient.UserProfiles.V1
            .GetUserProfileByXtremeIdiotsId(xtremeIdiotsId, cancellationToken)
            .ConfigureAwait(false);

        if (userProfileResponse.IsNotFound || userProfileResponse.Result?.Data is null)
            return null;

        return userProfileResponse.Result.Data;
    }

}