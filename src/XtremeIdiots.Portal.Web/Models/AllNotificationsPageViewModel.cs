namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// View model for the all notifications page with pagination
/// </summary>
/// <param name="Notifications">The list of notifications for the current page</param>
/// <param name="CurrentPage">The current page number (1-based)</param>
/// <param name="TotalPages">The total number of pages</param>
/// <param name="TotalCount">The total number of notifications</param>
/// <param name="UnreadCount">The number of unread notifications</param>
public record AllNotificationsPageViewModel(
    IList<NotificationViewModel> Notifications,
    int CurrentPage,
    int TotalPages,
    int TotalCount,
    int UnreadCount);
