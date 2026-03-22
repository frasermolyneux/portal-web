namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Represents a single notification displayed to a user
/// </summary>
/// <param name="NotificationId">Unique identifier for the notification</param>
/// <param name="Title">The notification title</param>
/// <param name="Message">The notification message body</param>
/// <param name="Icon">FontAwesome icon class for the notification type</param>
/// <param name="CreatedAt">When the notification was created</param>
/// <param name="IsRead">Whether the user has read this notification</param>
/// <param name="LinkUrl">Optional URL to navigate to when clicked</param>
/// <param name="LinkText">Optional text for the action link</param>
public record NotificationViewModel(
    Guid NotificationId,
    string Title,
    string Message,
    string Icon,
    DateTime CreatedAt,
    bool IsRead,
    string? LinkUrl = null,
    string? LinkText = null);
