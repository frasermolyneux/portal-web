namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// Represents a type of notification that users can configure preferences for
/// </summary>
/// <param name="NotificationTypeId">Unique identifier for the notification type</param>
/// <param name="DisplayName">Human-readable name shown to users</param>
/// <param name="Description">Description of what triggers this notification</param>
/// <param name="SupportsInSite">Whether in-site delivery is available</param>
/// <param name="SupportsEmail">Whether email delivery is available</param>
/// <param name="DefaultInSiteEnabled">Default in-site preference for new users</param>
/// <param name="DefaultEmailEnabled">Default email preference for new users</param>
public record NotificationTypeViewModel(
    string NotificationTypeId,
    string DisplayName,
    string Description,
    bool SupportsInSite,
    bool SupportsEmail,
    bool DefaultInSiteEnabled,
    bool DefaultEmailEnabled);
