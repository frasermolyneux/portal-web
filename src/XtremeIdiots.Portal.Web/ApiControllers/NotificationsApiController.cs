using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for notification operations (bell dropdown)
/// </summary>
[Authorize]
[Route("api/notifications")]
public class NotificationsApiController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<NotificationsApiController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    /// <summary>
    /// Gets the unread notification count for the current user
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileId = User.UserProfileId();
            if (string.IsNullOrEmpty(userProfileId))
            {
                return Ok(new { count = 0 });
            }

            var response = await repositoryApiClient.Notifications.V1
                .GetUnreadNotificationCount(Guid.Parse(userProfileId), cancellationToken)
                .ConfigureAwait(false);

            var count = response.Result?.Data ?? 0;

            return Ok(new { count });
        }, nameof(GetUnreadCount)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the most recent notifications for the current user
    /// </summary>
    /// <param name="take">Number of notifications to return (default 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileId = User.UserProfileId();
            if (string.IsNullOrEmpty(userProfileId))
            {
                return Ok(Array.Empty<object>());
            }

            var response = await repositoryApiClient.Notifications.V1
                .GetNotifications(Guid.Parse(userProfileId), null, 0, take, NotificationOrder.CreatedAtDesc, cancellationToken)
                .ConfigureAwait(false);

            if (response.Result?.Data?.Items is null)
            {
                return Ok(Array.Empty<object>());
            }

            var notifications = response.Result.Data.Items.Select(n => new
            {
                n.NotificationId,
                n.Title,
                n.Message,
                n.ActionUrl,
                n.IsRead,
                n.CreatedAt
            });

            return Ok(notifications);
        }, nameof(GetRecent)).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a single notification as read
    /// </summary>
    /// <param name="id">Notification ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("{id}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileId = User.UserProfileId();
            if (string.IsNullOrEmpty(userProfileId))
            {
                return Unauthorized();
            }

            await repositoryApiClient.Notifications.V1
                .MarkNotificationAsRead(id, cancellationToken)
                .ConfigureAwait(false);

            TrackSuccessTelemetry("NotificationMarkedAsRead", nameof(MarkAsRead), new Dictionary<string, string>
            {
                { "NotificationId", id.ToString() }
            });

            return Ok();
        }, nameof(MarkAsRead)).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks all notifications as read for the current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var userProfileId = User.UserProfileId();
            if (string.IsNullOrEmpty(userProfileId))
            {
                return Unauthorized();
            }

            await repositoryApiClient.Notifications.V1
                .MarkAllNotificationsAsRead(Guid.Parse(userProfileId), cancellationToken)
                .ConfigureAwait(false);

            TrackSuccessTelemetry("AllNotificationsMarkedAsRead", nameof(MarkAllAsRead));

            return Ok();
        }, nameof(MarkAllAsRead)).ConfigureAwait(false);
    }
}
