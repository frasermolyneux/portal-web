using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XtremeIdiots.Portal.Web.Helpers;

/// <summary>
/// Renders an expiry date with Active / Expired / Permanent badge inside a <span>.
/// Expiry status is determined server-side (authoritative).
/// Client-side JS re-formats the date portion in the user's locale.
/// Usage: <expiry-badge expires-utc="@Model.Expires" />
/// </summary>
[HtmlTargetElement("expiry-badge")]
public class ExpiryBadgeTagHelper : TagHelper
{
    [HtmlAttributeName("expires-utc")] public DateTime? ExpiresUtc { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (ExpiresUtc is null)
        {
            output.Content.SetHtmlContent("<span title=\"No expiry\">Never</span>");
            return;
        }

        var now = DateTime.UtcNow;
        var expired = ExpiresUtc.Value <= now;
        var utc = DateTime.SpecifyKind(ExpiresUtc.Value, DateTimeKind.Utc);
        var dateStr = utc.ToString("yyyy-MM-dd");

        var badgeClass = expired ? "text-bg-danger" : "text-bg-success";
        var badgeText = expired ? "Expired" : "Active";
        var status = expired ? "expired" : "active";
        var title = expired ? $"Expired on {dateStr}" : $"Expires on {dateStr}";

        output.Content.SetHtmlContent(
            $"<time datetime=\"{utc:o}\" data-dt=\"expiry\" data-dt-status=\"{status}\" title=\"{title}\">" +
            $"{dateStr} <span class=\"badge {badgeClass} ms-1\">{badgeText}</span></time>");
    }
}
