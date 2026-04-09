using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XtremeIdiots.Portal.Web.Helpers;

/// <summary>
/// Renders a <time> element with a UTC datetime that client-side JS localizes.
/// No server-side timezone conversion — the browser handles locale and timezone natively.
/// Usage: <time user-time utc="@model.Date" />
/// </summary>
[HtmlTargetElement("time", Attributes = AttributeName)]
public class UserTimeTagHelper : TagHelper
{
    internal const string AttributeName = "user-time";
    private const string UtcAttributeName = "utc";

    [HtmlAttributeName(UtcAttributeName)] public DateTime Utc { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "time";
        output.TagMode = TagMode.StartTagAndEndTag;

        var utc = DateTime.SpecifyKind(Utc, DateTimeKind.Utc);
        output.Attributes.SetAttribute("datetime", utc.ToString("o"));
        output.Attributes.SetAttribute("data-dt", "localized");

        // Server-rendered fallback for noscript users
        output.Content.SetContent($"{utc:yyyy-MM-dd HH:mm} UTC");

        output.Attributes.RemoveAll(AttributeName);
        output.Attributes.RemoveAll(UtcAttributeName);
    }
}
