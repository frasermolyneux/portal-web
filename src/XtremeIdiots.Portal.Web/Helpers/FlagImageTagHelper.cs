using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using MX.GeoLocation.Abstractions.Models.V1_1;
using System.Text.Encodings.Web;

namespace XtremeIdiots.Portal.Web.Helpers;

[HtmlTargetElement("flag-image")]
public class FlagImageTagHelper : TagHelper
{
    [HtmlAttributeName("country-code")] public string? CountryCode { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "img";
        var code = NormalizeCountryCode(CountryCode);
        output.Attributes.SetAttribute("src", $"/images/flags/{code}.png");
        output.TagMode = TagMode.SelfClosing;
    }

    private static string NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return "unknown";
        }

        var normalized = countryCode.Trim().ToLowerInvariant();
        var isTwoLetterCode = normalized.Length == 2
            && normalized[0] is >= 'a' and <= 'z'
            && normalized[1] is >= 'a' and <= 'z';

        return isTwoLetterCode ? normalized : "unknown";
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

        var code = NormalizeCountryCode(CountryCode);
        var encodedIpAddress = UrlEncoder.Default.Encode(Ip);
        var encodedIpText = HtmlEncoder.Default.Encode(Ip);
        List<string> parts =
        [
            $"<img src=\"/images/flags/{code}.png\" />",
            LinkToDetails ? $"<a href=\"/IPAddresses/Details?ipAddress={encodedIpAddress}\">{encodedIpText}</a>" : encodedIpText
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
            var encodedProxyType = HtmlEncoder.Default.Encode(ProxyType);
            parts.Add($"<span class=\"badge rounded-pill text-bg-primary\">{encodedProxyType}</span>");
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

    private static string NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return "unknown";
        }

        var normalized = countryCode.Trim().ToLowerInvariant();
        var isTwoLetterCode = normalized.Length == 2
            && normalized[0] is >= 'a' and <= 'z'
            && normalized[1] is >= 'a' and <= 'z';

        return isTwoLetterCode ? normalized : "unknown";
    }
}