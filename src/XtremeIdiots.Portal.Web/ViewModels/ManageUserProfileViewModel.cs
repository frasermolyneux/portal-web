using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Composite view model combining repository user profile data with ASP.NET Identity user information.
/// </summary>
public class ManageUserProfileViewModel
{
    public const string NotificationsTabName = "notifications";

    /// <summary>
    /// The repository user profile DTO.
    /// </summary>
    public UserProfileDto Profile { get; set; } = null!;

    /// <summary>
    /// Identity data for the associated user (may be partial / null if user not found in Identity store).
    /// </summary>
    public IdentityUserSummary? Identity { get; set; }

    /// <summary>
    /// All available additional permission definitions for the permission picker.
    /// </summary>
    public List<AdditionalPermissionDefinition> AvailablePermissions { get; set; } = [.. AdditionalPermission.Definitions];

    /// <summary>
    /// Game types the current admin user has authority to assign permissions for.
    /// </summary>
    public List<GameType> AssignableGameTypes { get; set; } = [];

    /// <summary>
    /// Claim rows projected for display, including whether the current actor may remove each claim.
    /// </summary>
    public List<ManageUserProfileClaimEntry> Claims { get; set; } = [];

    /// <summary>
    /// All notification types available for preference management, including supported channels and defaults.
    /// </summary>
    public List<NotificationTypeViewModel> NotificationTypes { get; set; } = [];

    /// <summary>
    /// Effective notification preferences to display for each notification type, preserving explicit values when present.
    /// </summary>
    public List<ManageUserNotificationPreferenceEntry> NotificationPreferences { get; set; } = [];

    /// <summary>
    /// Recent notification history for the managed user.
    /// </summary>
    public List<ManageUserNotificationHistoryEntry> RecentNotifications { get; set; } = [];

    /// <summary>
    /// Indicates whether the current actor may update notification preferences for the managed user.
    /// </summary>
    public bool CanUpdateNotificationPreferences { get; set; }

    /// <summary>
    /// Stable tab key preserved through redirects so a future tabbed UI can restore the notifications section.
    /// </summary>
    public string ActiveTab { get; set; } = string.Empty;

    /// <summary>
    /// Visible error message shown when notification preferences could not be loaded for the otherwise successful profile page.
    /// </summary>
    public string? NotificationPreferencesErrorMessage { get; set; }

    /// <summary>
    /// Visible error message shown when notification history could not be loaded for the otherwise successful profile page.
    /// </summary>
    public string? NotificationHistoryErrorMessage { get; set; }
}

/// <summary>
/// Lightweight projection of ASP.NET Identity user properties exposed to UI.
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

/// <summary>
/// Claim row data for the Manage User Profile screen.
/// </summary>
public class ManageUserProfileClaimEntry
{
    public Guid UserProfileClaimId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ScopeDisplayValue { get; set; } = string.Empty;
    public bool SystemGenerated { get; set; }
    public bool CanRemove { get; set; }
}

/// <summary>
/// Effective notification preference values for a specific notification type, with explicit overrides retained when present.
/// </summary>
public class ManageUserNotificationPreferenceEntry
{
    public string NotificationTypeId { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool? ExplicitInAppEnabled { get; set; }
    public bool? ExplicitEmailEnabled { get; set; }
}

/// <summary>
/// Read-only notification history row for the admin manage profile page.
/// </summary>
public class ManageUserNotificationHistoryEntry
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
/// Typed payload for updating notification preferences from the manage profile page.
/// </summary>
public class ManageUserNotificationPreferencesUpdateModel
{
    public Guid Id { get; set; }
    public List<ManageUserNotificationPreferenceUpdateEntry> Preferences { get; set; } = [];
}

/// <summary>
/// Posted channel values for a single notification type.
/// </summary>
public class ManageUserNotificationPreferenceUpdateEntry
{
    public string NotificationTypeId { get; set; } = string.Empty;
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
}
