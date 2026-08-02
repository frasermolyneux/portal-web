using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameTypeAuthorizationExtensions
{
    public static IReadOnlyList<GameType> DefinedGameTypes { get; } =
        [.. Enum.GetValues<GameType>().Where(gameType => gameType != GameType.Unknown)];

    public async static Task<IReadOnlyList<GameType>> GetAuthorizedGameTypesAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string policy)
    {
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(user);

        var authorizationTasks = DefinedGameTypes.Select(async gameType =>
        {
            var result = await authorizationService.AuthorizeAsync(user, gameType, policy).ConfigureAwait(false);
            return (GameType: gameType, result.Succeeded);
        });

        var results = await Task.WhenAll(authorizationTasks).ConfigureAwait(false);
        return [.. results.Where(result => result.Succeeded).Select(result => result.GameType)];
    }
}
