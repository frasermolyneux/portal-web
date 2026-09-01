using Microsoft.AspNetCore.Mvc.Rendering;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class HtmlHelpers
{

    public static string IsSelected(this IHtmlHelper html, string? controller = null, string? action = null, string? id = null, string? gameType = null, string? cssClass = null)
    {
        if (string.IsNullOrEmpty(cssClass))
            cssClass = "active";

        var currentAction = html.ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty;
        var currentController = html.ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var currentId = html.ViewContext.RouteData.Values["id"]?.ToString();
        var currentGameType = html.ViewContext.RouteData.Values["gameType"]?.ToString();

        if (string.IsNullOrEmpty(controller))
            controller = currentController;

        if (action is null)
        {
            if (controller == currentController)
            {

                return cssClass;
            }
        }

        else if (controller == currentController)
        {

            if (action == currentAction)
            {
                return (id == null || id == currentId) && (gameType == null || gameType == currentGameType) ? cssClass : string.Empty;
            }
        }

        return string.Empty;
    }

    public static string PageClass(this IHtmlHelper htmlHelper)
    {
        var currentAction = htmlHelper.ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty;
        return currentAction;
    }
}
