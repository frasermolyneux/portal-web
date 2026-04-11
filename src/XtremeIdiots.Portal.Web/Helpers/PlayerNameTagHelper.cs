using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement("player-name")]
public partial class PlayerNameTagHelper : TagHelper
{
    [HtmlAttributeName("value")] public string? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "cod-colored");

        if (string.IsNullOrWhiteSpace(Value))
        {
            output.Content.SetContent(string.Empty);
            return;
        }

        output.Content.SetHtmlContent(CodColorHelper.RenderColorCodes(Value));
    }
}

internal static partial class CodColorHelper
{
    [GeneratedRegex(@"\^([0-9])")]
    private static partial Regex colorCodeRegex();

    public static string RenderColorCodes(string input)
    {
        var matches = colorCodeRegex().Matches(input);
        if (matches.Count == 0)
            return HttpUtility.HtmlEncode(input);

        var result = new StringBuilder();
        var lastIndex = 0;
        int? currentColor = null;

        foreach (var match in matches.Cast<Match>())
        {
            var textBefore = input[lastIndex..match.Index];
            if (textBefore.Length > 0)
            {
                var encoded = HttpUtility.HtmlEncode(textBefore);
                if (currentColor.HasValue)
                    result.Append($"<span class=\"cod-color-{currentColor.Value}\">{encoded}</span>");
                else
                    result.Append(encoded);
            }

            currentColor = int.Parse(match.Groups[1].Value);
            lastIndex = match.Index + match.Length;
        }

        var remaining = input[lastIndex..];
        if (remaining.Length > 0)
        {
            var encoded = HttpUtility.HtmlEncode(remaining);
            if (currentColor.HasValue)
                result.Append($"<span class=\"cod-color-{currentColor.Value}\">{encoded}</span>");
            else
                result.Append(encoded);
        }

        return result.ToString();
    }
}
