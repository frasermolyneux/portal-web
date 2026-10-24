using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed record PlayerProfileResult(Guid? PlayerId, string? Username, string Status, DateTime ExpiresAtUtc);

public interface IPlayerProfileService
{
    Task<PlayerProfileResult> GetAsync(GameType gameType, string? playerGuid, CancellationToken cancellationToken = default);
}
