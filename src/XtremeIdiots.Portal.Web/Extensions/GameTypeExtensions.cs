using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameTypeExtensions
{
    public static string? RedirectManagerUrl(this GameType gameType, string? mapName = null)
    {
        if (gameType is not (GameType.CallOfDuty4 or GameType.CallOfDuty4x or GameType.CallOfDuty5) ||
            (mapName is not null && BuiltInMaps.IsBuiltIn(gameType, mapName)))
            return null;

        var game = gameType == GameType.CallOfDuty5 ? "cod5" : "cod4";
        var query = new Dictionary<string, string?> { ["game"] = game };
        if (!string.IsNullOrWhiteSpace(mapName))
            query["map"] = mapName;
        return QueryHelpers.AddQueryString("https://maps.xtremeidiots.net/", query);
    }

    public static string ToDisplayName(this GameType gameType)
    {
        var memberInfo = typeof(GameType).GetMember(gameType.ToString());
        if (memberInfo.Length > 0)
        {
            var displayAttr = memberInfo[0]
                .GetCustomAttributes(typeof(DisplayAttribute), false)
                .FirstOrDefault() as DisplayAttribute;
            if (displayAttr?.Name is not null)
                return displayAttr.Name;
        }

        return gameType.ToString();
    }
}
