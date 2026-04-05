using Microsoft.AspNetCore.Html;
using System.Text;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Web.Models;

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
            sb.Append($"<a href=\"/IPAddresses/Details?ipAddress={ipAddress}\">{ipAddress}</a>");
        }
        else
        {
            sb.Append(ipAddress);
        }

        if (riskScore.HasValue)
        {
            var riskClass = GetRiskClass(riskScore.Value);
            sb.Append($" <span class=\"badge rounded-pill {riskClass}\">Risk: {riskScore}</span>");
        }

        if (!string.IsNullOrEmpty(proxyType))
        {
            sb.Append($" <span class=\"badge rounded-pill text-bg-primary\">{proxyType}</span>");
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

    public static HtmlString FormatIPAddress(
        this PlayerDto player,
        string? countryCode = null,
        bool linkToDetails = true)
    {
        return player is null || string.IsNullOrEmpty(player.IpAddress)
            ? HtmlString.Empty
            : FormatIPAddress(
            player.IpAddress,
            countryCode ?? player.CountryCode(),
            player.ProxyCheckRiskScore(),
            player.IsProxy(),
            player.IsVpn(),
            player.ProxyType(),
            linkToDetails);
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