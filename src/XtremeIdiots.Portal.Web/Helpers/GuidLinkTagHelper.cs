using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement("guid-link")]
public class GuidLinkTagHelper : TagHelper
{
    [HtmlAttributeName("guid")] public string? GuidValue { get; set; }
    [HtmlAttributeName("game")] public string? Game { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(GuidValue))
        {
            output.SuppressOutput();
            return;
        }
        if (Game?.Equals("CallOfDuty4", StringComparison.OrdinalIgnoreCase) == true)
        {
            var link = $"https://www.pbbans.com/mbi.php?action=12&guid={GuidValue}";
            output.TagName = "a";
            output.Attributes.SetAttribute("href", link);
            output.Attributes.SetAttribute("target", "_blank");
            output.Attributes.SetAttribute("style", "margin:5px");
            output.Content.SetContent(GuidValue);
        }
        else if (Game?.Equals("CallOfDuty4x", StringComparison.OrdinalIgnoreCase) == true
                 && GuidValue.Length == 17
                 && GuidValue.StartsWith("7656119", StringComparison.Ordinal)
                 && GuidValue.All(char.IsDigit))
        {
            var link = $"https://steamcommunity.com/profiles/{GuidValue}";
            output.TagName = "a";
            output.Attributes.SetAttribute("href", link);
            output.Attributes.SetAttribute("target", "_blank");
            output.Attributes.SetAttribute("style", "margin:5px");
            output.Content.SetContent(GuidValue);
        }
        else
        {
            output.TagName = "span";
            output.Content.SetContent(GuidValue);
        }
    }
}
