namespace XtremeIdiots.Portal.Web.Models;

/// <summary>
/// View model for the profile manage page with connected-player linking state.
/// </summary>
/// <param name="UserProfileId">Current portal user profile id, if available</param>
/// <param name="ActiveActivationCode">Current active activation code, if available</param>
/// <param name="LinkedPlayers">Connected player links for the current profile</param>
/// <param name="TotalLinkedPlayers">Total linked players reported by the API for this profile</param>
/// <param name="IsLinkedPlayersCapped">Indicates whether the linked players list shown in UI is capped</param>
public record ProfileManageViewModel(
    Guid? UserProfileId,
    ProfileActivationCodeViewModel? ActiveActivationCode,
    IList<ProfileLinkedPlayerViewModel> LinkedPlayers,
    int TotalLinkedPlayers,
    bool IsLinkedPlayersCapped);

public record ProfileActivationCodeViewModel(
    string Code,
    DateTime ExpiresAtUtc,
    bool IsActive);

public record ProfileLinkedPlayerViewModel(
    string GameType,
    string Username,
    string LinkMethod,
    DateTime LinkedAtUtc,
    DateTime? UnlinkedAtUtc,
    bool IsActive);
