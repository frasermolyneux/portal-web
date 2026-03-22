namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Represents a notification type available in the system.
/// </summary>
public class NotificationTypeEntry
{
    public Guid NotificationTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool SupportsEmail { get; set; }
    public bool SupportsInApp { get; set; }
}

/// <summary>
/// Represents a user's preference for a specific notification type.
/// </summary>
public class NotificationPreferenceEntry
{
    public Guid NotificationTypeId { get; set; }
    public bool EmailEnabled { get; set; }
    public bool InAppEnabled { get; set; }
}

/// <summary>
/// Represents a single notification sent to a user.
/// </summary>
public class NotificationEntry
{
    public Guid NotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
    public bool EmailSent { get; set; }
}

/// <summary>
/// View model for the admin Manage Notifications page.
/// Combines user profile, notification preferences, and notification history.
/// </summary>
public class ManageUserNotificationsViewModel
{
    /// <summary>
    /// The user profile ID being managed.
    /// </summary>
    public Guid UserProfileId { get; set; }

    /// <summary>
    /// Display name of the user being managed.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// All notification types available in the system.
    /// </summary>
    public List<NotificationTypeEntry> NotificationTypes { get; set; } = [];

    /// <summary>
    /// The user's current notification preferences.
    /// </summary>
    public List<NotificationPreferenceEntry> Preferences { get; set; } = [];

    /// <summary>
    /// Recent notifications sent to the user.
    /// </summary>
    public List<NotificationEntry> Notifications { get; set; } = [];
}
