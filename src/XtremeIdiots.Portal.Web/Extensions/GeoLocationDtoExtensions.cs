using Microsoft.AspNetCore.Html;
using MX.GeoLocation.Abstractions.Models.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GeoLocationDtoExtensions
{
    public static HtmlString FlagImage(this GeoLocationDto geoLocationDto)
    {
        var countryCode = NormalizeCountryCode(geoLocationDto.CountryCode);
        return new HtmlString($"<img src=\"/images/flags/{countryCode}.png\" />");
    }

    public static HtmlString FlagImage(this string? countryCode)
    {
        var normalizedCountryCode = NormalizeCountryCode(countryCode);
        return new HtmlString($"<img src=\"/images/flags/{normalizedCountryCode}.png\" />");
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

    public static HtmlString LocationSummary(this GeoLocationDto geoLocationDto)
    {
        return !string.IsNullOrWhiteSpace(geoLocationDto.CityName) &&
            !string.IsNullOrWhiteSpace(geoLocationDto.CountryName)
            ? new HtmlString($"{geoLocationDto.CityName}, {geoLocationDto.CountryName}")
            : !string.IsNullOrWhiteSpace(geoLocationDto.CountryCode)
            ? new HtmlString($"{geoLocationDto.CountryCode}")
            : !string.IsNullOrWhiteSpace(geoLocationDto.RegisteredCountry)
            ? new HtmlString($"{geoLocationDto.RegisteredCountry}")
            : new HtmlString("Unknown");
    }
}