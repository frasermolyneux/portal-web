using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// External notifications API for the xtremeidiots.com forum widget.
/// Returns a public feed for unauthenticated requests, or personalised
/// permission-scoped notifications when a valid HMAC-signed forum token is provided.
/// </summary>
[Route("api/external/notifications")]
public class ExternalNotificationsController(
    IRepositoryApiClient repositoryApiClient,
    IExternalTokenService externalTokenService,
    TelemetryClient telemetryClient,
    ILogger<ExternalNotificationsController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    private readonly string portalBaseUrl = (configuration["XtremeIdiots:PortalBaseUrl"] ?? "https://portal.xtremeidiots.com").TrimEnd('/');

    /// <summary>
    /// Gets notifications for the external forum widget.
    /// If a valid HMAC token is provided, returns personalised notifications.
    /// Otherwise returns a public feed of recent admin actions.
    /// </summary>
    [HttpGet]
    [EnableCors("CorsPolicy")]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] string? token = null,
        [FromQuery] int take = 15,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            take = Math.Clamp(take, 1, 50);

            // If no token provided, return public feed
            if (string.IsNullOrEmpty(token))
                return Ok(await BuildPublicFeedAsync(take, cancellationToken).ConfigureAwait(false));

            // Validate HMAC token
            var tokenResult = externalTokenService.ValidateToken(token);
            if (!tokenResult.IsValid || tokenResult.ForumMemberId is null)
            {
                Logger.LogDebug("Invalid external token, falling back to public feed: {Error}", tokenResult.Error);
                return Ok(await BuildPublicFeedAsync(take, cancellationToken).ConfigureAwait(false));
            }

            // Look up the portal user by forum member ID
            var userResult = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(tokenResult.ForumMemberId, cancellationToken)
                .ConfigureAwait(false);

            if (userResult.IsNotFound || userResult.Result?.Data is null)
            {
                Logger.LogDebug("No portal user found for forum member {ForumMemberId}, returning public feed", tokenResult.ForumMemberId);
                return Ok(await BuildPublicFeedAsync(take, cancellationToken).ConfigureAwait(false));
            }

            var userProfile = userResult.Result.Data;

            // Return personalised notifications
            var response = await BuildPersonalisedFeedAsync(userProfile.UserProfileId, userProfile.DisplayName, take, cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("ExternalNotificationsAuthenticated", nameof(GetNotifications), new Dictionary<string, string>
            {
                { "ForumMemberId", tokenResult.ForumMemberId },
                { "UserProfileId", userProfile.UserProfileId.ToString() }
            });

            return Ok(response);
        }, nameof(GetNotifications)).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a notification as read from the external widget
    /// </summary>
    [HttpPost("{id:guid}/read")]
    [EnableCors("CorsPolicy")]
    public async Task<IActionResult> MarkAsRead(
        Guid id,
        [FromBody] ExternalMarkAsReadRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (string.IsNullOrEmpty(request.Token))
                return Unauthorized();

            var tokenResult = externalTokenService.ValidateToken(request.Token);
            if (!tokenResult.IsValid || tokenResult.ForumMemberId is null)
                return Unauthorized();

            // Verify the notification belongs to this user
            var userResult = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(tokenResult.ForumMemberId, cancellationToken)
                .ConfigureAwait(false);

            if (userResult.IsNotFound || userResult.Result?.Data is null)
                return Unauthorized();

            var userProfileId = userResult.Result.Data.UserProfileId;

            // Verify the notification belongs to this user by fetching their notifications
            // and checking the target ID is among them
            var notificationsResult = await repositoryApiClient.Notifications.V1
                .GetNotifications(userProfileId, null, 0, 100, null, cancellationToken)
                .ConfigureAwait(false);

            var userOwnsNotification = notificationsResult.Result?.Data?.Items?
                .Any(n => n.NotificationId == id) ?? false;

            if (!userOwnsNotification)
            {
                Logger.LogWarning("External mark-as-read rejected: notification {NotificationId} does not belong to user {UserProfileId}", id, userProfileId);
                return NotFound();
            }

            await repositoryApiClient.Notifications.V1
                .MarkNotificationAsRead(id, cancellationToken)
                .ConfigureAwait(false);

            TrackSuccessTelemetry("ExternalNotificationMarkedAsRead", nameof(MarkAsRead), new Dictionary<string, string>
            {
                { "NotificationId", id.ToString() },
                { "ForumMemberId", tokenResult.ForumMemberId }
            });

            return Ok();
        }, nameof(MarkAsRead)).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks all notifications as read for the authenticated forum user
    /// </summary>
    [HttpPost("read-all")]
    [EnableCors("CorsPolicy")]
    public async Task<IActionResult> MarkAllAsRead(
        [FromBody] ExternalMarkAsReadRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (string.IsNullOrEmpty(request.Token))
                return Unauthorized();

            var tokenResult = externalTokenService.ValidateToken(request.Token);
            if (!tokenResult.IsValid || tokenResult.ForumMemberId is null)
                return Unauthorized();

            var userResult = await repositoryApiClient.UserProfiles.V1
                .GetUserProfileByXtremeIdiotsId(tokenResult.ForumMemberId, cancellationToken)
                .ConfigureAwait(false);

            if (userResult.IsNotFound || userResult.Result?.Data is null)
                return Unauthorized();

            await repositoryApiClient.Notifications.V1
                .MarkAllNotificationsAsRead(userResult.Result.Data.UserProfileId, cancellationToken)
                .ConfigureAwait(false);

            TrackSuccessTelemetry("ExternalAllNotificationsMarkedAsRead", nameof(MarkAllAsRead), new Dictionary<string, string>
            {
                { "ForumMemberId", tokenResult.ForumMemberId }
            });

            return Ok();
        }, nameof(MarkAllAsRead)).ConfigureAwait(false);
    }

    private async Task<object> BuildPublicFeedAsync(int take, CancellationToken cancellationToken)
    {
        var adminActionsResult = await repositoryApiClient.AdminActions.V1
            .GetAdminActions(null, null, null, null, 0, take, AdminActionOrder.CreatedDesc, cancellationToken)
            .ConfigureAwait(false);

        var publicNotifications = new List<object>();

        if (adminActionsResult.IsSuccess && adminActionsResult.Result?.Data?.Items is not null)
        {
            foreach (var action in adminActionsResult.Result.Data.Items)
            {
                var actionText = action.Expires <= DateTime.UtcNow &&
                    (action.Type == AdminActionType.Ban || action.Type == AdminActionType.TempBan)
                    ? $"lifted a {action.Type} on"
                    : $"added a {action.Type} to";

                publicNotifications.Add(new
                {
                    type = "admin-action",
                    actionType = action.Type.ToString(),
                    gameType = action.Player?.GameType.ToString(),
                    title = $"{action.UserProfile?.DisplayName ?? "Admin"} {actionText} {action.Player?.Username}",
                    message = $"{action.Type} on {action.Player?.GameType}",
                    iconUrl = $"{portalBaseUrl}/images/game-icons/{action.Player?.GameType}.png",
                    actionUrl = $"{portalBaseUrl}/Players/Details/{action.PlayerId}",
                    createdAt = action.Created
                });
            }
        }

        TrackSuccessTelemetry("ExternalNotificationsPublic", nameof(GetNotifications), new Dictionary<string, string>
        {
            { "Count", publicNotifications.Count.ToString() }
        });

        return new
        {
            authenticated = false,
            notifications = publicNotifications
        };
    }

    private async Task<object> BuildPersonalisedFeedAsync(Guid userProfileId, string? displayName, int take, CancellationToken cancellationToken)
    {
        // Fetch user's notifications
        var notificationsResult = await repositoryApiClient.Notifications.V1
            .GetNotifications(userProfileId, null, 0, take, NotificationOrder.CreatedAtDesc, cancellationToken)
            .ConfigureAwait(false);

        var unreadCountResult = await repositoryApiClient.Notifications.V1
            .GetUnreadNotificationCount(userProfileId, cancellationToken)
            .ConfigureAwait(false);

        var notifications = new List<object>();

        if (notificationsResult.Result?.Data?.Items is not null)
        {
            foreach (var n in notificationsResult.Result.Data.Items)
            {
                notifications.Add(new
                {
                    id = n.NotificationId,
                    type = n.NotificationTypeId,
                    title = n.Title,
                    message = n.Message,
                    actionUrl = n.ActionUrl is not null ? $"{portalBaseUrl}{n.ActionUrl}" : null,
                    createdAt = n.CreatedAt,
                    isRead = n.IsRead
                });
            }
        }

        // Check for unclaimed actions (for admins)
        var unclaimedResult = await repositoryApiClient.AdminActions.V1
            .GetAdminActions(null, null, null, AdminActionFilter.UnclaimedActions, 0, 1, null, cancellationToken)
            .ConfigureAwait(false);

        var hasUnclaimed = unclaimedResult.IsSuccess &&
            unclaimedResult.Result?.Data?.Items is not null &&
            unclaimedResult.Result.Data.Items.Any();

        return new
        {
            authenticated = true,
            displayName,
            unreadCount = unreadCountResult.Result?.Data ?? 0,
            notifications,
            unclaimed = new
            {
                hasItems = hasUnclaimed,
                url = $"{portalBaseUrl}/AdminActions/Unclaimed"
            },
            portalUrl = portalBaseUrl
        };
    }
}

/// <summary>
/// Request body for external mark-as-read operations
/// </summary>
public record ExternalMarkAsReadRequest
{
    public string? Token { get; init; }
}
