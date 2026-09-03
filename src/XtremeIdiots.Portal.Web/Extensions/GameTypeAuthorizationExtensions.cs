using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameTypeAuthorizationExtensions
{
    public static IReadOnlyList<GameType> DefinedGameTypes { get; } =
        [.. Enum.GetValues<GameType>().Where(gameType => gameType != GameType.Unknown)];

    public static IReadOnlyList<GameType> TeamAccessGameTypes { get; } =
        [GameType.CallOfDuty2, GameType.CallOfDuty4, GameType.CallOfDuty5];

    public static GameType NormalizeTeamAccessGameType(GameType gameType)
    {
        return gameType == GameType.CallOfDuty4x
            ? GameType.CallOfDuty4
            : gameType;
    }

    public static IReadOnlyList<GameType> GetTeamAccessIncludedGameTypes(GameType gameType)
    {
        return NormalizeTeamAccessGameType(gameType) == GameType.CallOfDuty4
            ? [GameType.CallOfDuty4, GameType.CallOfDuty4x]
            : [gameType];
    }

    public async static Task<IReadOnlyList<GameType>> GetAuthorizedTeamAccessGameTypesAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        string policy)
    {
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(user);

        var authorizationTasks = TeamAccessGameTypes.Select(async gameType =>
        {
            var result = await authorizationService.AuthorizeAsync(user, gameType, policy).ConfigureAwait(false);
            return (GameType: gameType, Succeeded: result?.Succeeded == true);
        });

        var results = await Task.WhenAll(authorizationTasks).ConfigureAwait(false);
        return [.. results.Where(result => result.Succeeded).Select(result => result.GameType)];
    }

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
