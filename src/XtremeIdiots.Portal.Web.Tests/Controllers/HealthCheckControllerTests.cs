using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class HealthCheckControllerTests
{
    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Act
        var sut = new HealthCheckController();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public void Status_ReturnsRedirectResult()
    {
        // Arrange
        var sut = new HealthCheckController();

        // Act
        var result = sut.Status();

        // Assert
        var redirectResult = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/api/healthcheck/status", redirectResult.Url);
        Assert.True(redirectResult.Permanent);
    }
}
