using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameTypeExtensions
{
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
