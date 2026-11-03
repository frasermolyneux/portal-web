using Ganss.Xss;

namespace XtremeIdiots.Portal.Web.Helpers;

public static class AdminActionHtmlSanitizer
{
    public static string Sanitize(string? html)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.IntersectWith(
        [
            "a", "b", "blockquote", "br", "code", "div", "em", "h1", "h2", "h3", "h4", "h5", "h6",
            "i", "img", "li", "ol", "p", "pre", "s", "span", "strike", "strong", "table", "tbody",
            "td", "th", "thead", "tr", "u", "ul"
        ]);
        sanitizer.AllowedAttributes.IntersectWith(["alt", "height", "href", "src", "style", "title", "width"]);
        sanitizer.AllowedCssProperties.IntersectWith(
        [
            "background-color", "color", "font-family", "font-size", "font-style", "font-weight",
            "text-align", "text-decoration"
        ]);
        return sanitizer.Sanitize(html ?? string.Empty);
    }
}
