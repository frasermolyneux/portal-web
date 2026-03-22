namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Represents a user's notification preference for a specific notification type
/// </summary>
/// <param name="NotificationTypeId">The notification type this preference applies to</param>
/// <param name="InSiteEnabled">Whether in-site notifications are enabled</param>
/// <param name="EmailEnabled">Whether email notifications are enabled</param>
public record NotificationPreferenceViewModel(
    string NotificationTypeId,
    bool InSiteEnabled,
    bool EmailEnabled);
