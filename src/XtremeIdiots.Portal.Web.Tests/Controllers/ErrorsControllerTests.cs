using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class ErrorsControllerTests
{
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<ErrorsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly Mock<IWebHostEnvironment> mockWebHostEnvironment = new();

    private ErrorsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new ErrorsController(
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var sut = CreateSut();

        // Assert
        Assert.NotNull(sut);
    }

    [Fact]
    public void Constructor_WithNullTelemetryClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ErrorsController(null!, mockLogger.Object, mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ErrorsController(telemetryClient, null!, mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ErrorsController(telemetryClient, mockLogger.Object, null!));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public void Display_WithStandardUser_ReturnsViewResult(int statusCode)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Display(statusCode, mockWebHostEnvironment.Object);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(statusCode, viewResult.Model);
    }

    [Fact]
    public void Display_WithSeniorAdmin_NoExceptionContext_ReturnsViewResult()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim("SeniorAdmin", "true"));
        var user = new ClaimsPrincipal(identity);
        var sut = CreateSut(user);

        // Act
        var result = sut.Display(500, mockWebHostEnvironment.Object);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(500, viewResult.Model);
    }

    [Fact]
    public void Display_WithSeniorAdmin_WithExceptionContext_ReturnsProblemDetails()
    {
        // Arrange
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim("SeniorAdmin", "true"));
        var user = new ClaimsPrincipal(identity);
        var sut = CreateSut(user);

        var exception = new InvalidOperationException("Test error message");
        var mockExceptionFeature = new Mock<IExceptionHandlerFeature>();
        mockExceptionFeature.Setup(f => f.Error).Returns(exception);
        sut.HttpContext.Features.Set(mockExceptionFeature.Object);

        // Act
        var result = sut.Display(500, mockWebHostEnvironment.Object);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Test error message", problemDetails.Title);
    }
}
