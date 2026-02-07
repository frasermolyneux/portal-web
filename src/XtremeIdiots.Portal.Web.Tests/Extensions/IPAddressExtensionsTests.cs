using XtremeIdiots.Portal.Web.Extensions;
using MX.GeoLocation.Abstractions.Models.V1;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class IPAddressExtensionsTests
{
    [Fact]
    public void FormatIPAddress_WithValidIPAddress_ReturnsHtmlString()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress();

        // Assert
        Assert.NotNull(result);
        Assert.Contains(ipAddress, result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithNullIPAddress_ReturnsEmpty()
    {
        // Arrange
        string? ipAddress = null;

        // Act
        var result = ipAddress.FormatIPAddress();

        // Assert
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithEmptyIPAddress_ReturnsEmpty()
    {
        // Arrange
        var ipAddress = string.Empty;

        // Act
        var result = ipAddress.FormatIPAddress();

        // Assert
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithNullGeoLocation_ReturnsValidResult()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress(geoLocation: null);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(ipAddress, result.Value);
        Assert.Contains("unknown.png", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithRiskScore_IncludesRiskBadge()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var riskScore = 50;

        // Act
        var result = ipAddress.FormatIPAddress(riskScore: riskScore);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Risk:", result.Value);
        Assert.Contains("50", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithProxyFlag_IncludesProxyBadge()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress(isProxy: true);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Proxy", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithVpnFlag_IncludesVpnBadge()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress(isVpn: true);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("VPN", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithProxyType_IncludesProxyTypeBadge()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var proxyType = "SOCKS5";

        // Act
        var result = ipAddress.FormatIPAddress(proxyType: proxyType);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(proxyType, result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithLinkToDetailsTrue_IncludesLink()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress(linkToDetails: true);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("/IPAddresses/Details", result.Value);
        Assert.Contains("<a", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithLinkToDetailsFalse_DoesNotIncludeLink()
    {
        // Arrange
        var ipAddress = "192.168.1.1";

        // Act
        var result = ipAddress.FormatIPAddress(linkToDetails: false);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("<a", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithHighRiskScore_UsesCorrectBadgeClass()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var riskScore = 85;

        // Act
        var result = ipAddress.FormatIPAddress(riskScore: riskScore);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("text-bg-danger", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithMediumRiskScore_UsesCorrectBadgeClass()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var riskScore = 60;

        // Act
        var result = ipAddress.FormatIPAddress(riskScore: riskScore);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("text-bg-warning", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithLowRiskScore_UsesCorrectBadgeClass()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var riskScore = 30;

        // Act
        var result = ipAddress.FormatIPAddress(riskScore: riskScore);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("text-bg-info", result.Value);
    }

    [Fact]
    public void FormatIPAddress_WithVeryLowRiskScore_UsesCorrectBadgeClass()
    {
        // Arrange
        var ipAddress = "192.168.1.1";
        var riskScore = 10;

        // Act
        var result = ipAddress.FormatIPAddress(riskScore: riskScore);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("text-bg-success", result.Value);
    }
}
