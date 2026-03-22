namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// View model for the notification preferences page
/// </summary>
/// <param name="UserProfileId">The current user's profile ID</param>
/// <param name="NotificationTypes">Available notification types</param>
/// <param name="Preferences">The user's current notification preferences</param>
public record NotificationPreferencesPageViewModel(
    Guid UserProfileId,
    IList<NotificationTypeViewModel> NotificationTypes,
    IList<NotificationPreferenceViewModel> Preferences);
