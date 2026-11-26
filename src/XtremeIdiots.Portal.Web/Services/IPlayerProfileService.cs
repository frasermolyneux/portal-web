using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed record PlayerProfileTag(Guid? TagId, string? Name, string? TagHtml);

public sealed record PlayerProfileResult(Guid? PlayerId, string? Username, string Status, DateTime ExpiresAtUtc)
{
    public IReadOnlyList<PlayerProfileTag> Tags { get; init; } = [];
}

public interface IPlayerProfileService
{
    Task<PlayerProfileResult> GetAsync(GameType gameType, string? playerGuid, CancellationToken cancellationToken = default);
}
