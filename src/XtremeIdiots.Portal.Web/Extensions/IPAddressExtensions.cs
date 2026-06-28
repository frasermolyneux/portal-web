using Microsoft.AspNetCore.Html;
using System.Text;
using System.Text.Encodings.Web;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class IPAddressExtensions
{

    public static HtmlString FormatIPAddress(
        this string ipAddress,
        string? countryCode = null,
        int? riskScore = null,
        bool? isProxy = null,
        bool? isVpn = null,
        string? proxyType = null,
        bool linkToDetails = true)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return HtmlString.Empty;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            sb.Append(countryCode.FlagImage().Value);
            sb.Append(' ');
        }
        else
        {
            sb.Append("<img src=\"/images/flags/unknown.png\" /> ");
        }

        if (linkToDetails)
        {
            var encodedIpAddress = UrlEncoder.Default.Encode(ipAddress);
            var encodedIpText = HtmlEncoder.Default.Encode(ipAddress);
            sb.Append($"<a href=\"/IPAddresses/Details?ipAddress={encodedIpAddress}\">{encodedIpText}</a>");
        }
        else
        {
            sb.Append(HtmlEncoder.Default.Encode(ipAddress));
        }

        if (riskScore.HasValue)
        {
            var riskClass = GetRiskClass(riskScore.Value);
            sb.Append($" <span class=\"badge rounded-pill {riskClass}\">Risk: {riskScore}</span>");
        }

        if (!string.IsNullOrEmpty(proxyType))
        {
            var encodedProxyType = HtmlEncoder.Default.Encode(proxyType);
            sb.Append($" <span class=\"badge rounded-pill text-bg-primary\">{encodedProxyType}</span>");
        }

        if (isProxy == true)
        {
            sb.Append(" <span class=\"badge rounded-pill text-bg-danger\">Proxy</span>");
        }

        if (isVpn == true)
        {
            sb.Append(" <span class=\"badge rounded-pill text-bg-warning\">VPN</span>");
        }

        return new HtmlString(sb.ToString());
    }

    public static string GetRiskClass(int riskScore)
    {
        return riskScore switch
        {
            >= 80 => "text-bg-danger",
            >= 50 => "text-bg-warning",
            >= 25 => "text-bg-info",
            _ => "text-bg-success"
        };
    }
}