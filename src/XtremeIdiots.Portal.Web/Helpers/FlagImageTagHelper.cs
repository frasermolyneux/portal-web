using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using MX.GeoLocation.Abstractions.Models.V1_1;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement("flag-image")]
public class FlagImageTagHelper : TagHelper
{
    [HtmlAttributeName("country-code")] public string? CountryCode { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "img";
        var code = string.IsNullOrWhiteSpace(CountryCode) ? "unknown" : CountryCode.ToLower();
        output.Attributes.SetAttribute("src", $"/images/flags/{code}.png");
        output.TagMode = TagMode.SelfClosing;
    }
}

[HtmlTargetElement("location-summary", Attributes = "intelligence-model")]
public class LocationSummaryTagHelper : TagHelper
{
    [HtmlAttributeName("intelligence-model")] public IpIntelligenceDto? Intelligence { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        if (Intelligence is null)
        {
            output.Content.SetContent("Unknown");
            return;
        }

        var text = !string.IsNullOrWhiteSpace(Intelligence.CityName) && !string.IsNullOrWhiteSpace(Intelligence.CountryName)
            ? $"{Intelligence.CityName}, {Intelligence.CountryName}"
            : !string.IsNullOrWhiteSpace(Intelligence.CountryCode)
                ? Intelligence.CountryCode
                : "Unknown";
        output.Content.SetContent(text);
    }
}

[HtmlTargetElement("ip-address", Attributes = "ip")]
public class IpAddressTagHelper : TagHelper
{
    [HtmlAttributeName("ip")] public string? Ip { get; set; }
    [HtmlAttributeName("country-code")] public string? CountryCode { get; set; }
    [HtmlAttributeName("risk")] public int? Risk { get; set; }
    [HtmlAttributeName("is-proxy")] public bool? IsProxy { get; set; }
    [HtmlAttributeName("is-vpn")] public bool? IsVpn { get; set; }
    [HtmlAttributeName("proxy-type")] public string? ProxyType { get; set; }
    [HtmlAttributeName("link-to-details")] public bool LinkToDetails { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        if (string.IsNullOrEmpty(Ip))
        {
            output.SuppressOutput();
            return;
        }

        var code = string.IsNullOrEmpty(CountryCode) ? "unknown" : CountryCode.ToLower();
        List<string> parts =
        [
            $"<img src=\"/images/flags/{code}.png\" />",
            LinkToDetails ? $"<a href=\"/IPAddresses/Details?ipAddress={Ip}\">{Ip}</a>" : Ip
        ];

        if (Risk.HasValue)
        {
            var riskClass = Risk.Value switch
            {
                >= 80 => "text-bg-danger",
                >= 50 => "text-bg-warning",
                >= 25 => "text-bg-info",
                _ => "text-bg-success"
            };
            parts.Add($"<span class=\"badge rounded-pill {riskClass}\">Risk: {Risk}</span>");
        }

        if (!string.IsNullOrEmpty(ProxyType))
        {
            parts.Add($"<span class=\"badge rounded-pill text-bg-primary\">{ProxyType}</span>");
        }
        if (IsProxy == true)
        {
            parts.Add("<span class=\"badge rounded-pill text-bg-danger\">Proxy</span>");
        }

        if (IsVpn == true)
        {
            parts.Add("<span class=\"badge rounded-pill text-bg-warning\">VPN</span>");
        }


        output.Content.SetHtmlContent(string.Join(' ', parts));
    }
}