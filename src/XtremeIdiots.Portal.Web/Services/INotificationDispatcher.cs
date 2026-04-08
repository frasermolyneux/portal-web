using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Services;

/// <summary>
/// Represents the context for an admin action notification trigger
/// </summary>
public record AdminActionNotificationContext(
    GameType GameType,
    AdminActionType ActionType,
    string PlayerName,
    Guid PlayerId,
    string? AdminDisplayName,
    Guid? AdminUserProfileId);

/// <summary>
/// Dispatches notifications to relevant users based on events in the portal
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches notifications for a new admin action creation
    /// </summary>
    Task DispatchAdminActionCreatedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches notifications for an admin action being claimed
    /// </summary>
    Task DispatchAdminActionClaimedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches notifications for an admin action being lifted
    /// </summary>
    Task DispatchAdminActionLiftedAsync(AdminActionNotificationContext context, CancellationToken cancellationToken = default);
}
