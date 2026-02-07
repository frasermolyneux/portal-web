using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public class ProxyCheckServiceTests
{
    private readonly Mock<IHttpClientFactory> mockHttpClientFactory;
    private readonly Mock<IMemoryCache> mockMemoryCache;
    private readonly Mock<IConfiguration> mockConfiguration;
    private readonly Mock<ILogger<ProxyCheckService>> mockLogger;

    public ProxyCheckServiceTests()
    {
        mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockMemoryCache = new Mock<IMemoryCache>();
        mockConfiguration = new Mock<IConfiguration>();
        mockLogger = new Mock<ILogger<ProxyCheckService>>();
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProxyCheckService(null!, mockMemoryCache.Object, mockConfiguration.Object, mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMemoryCache_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProxyCheckService(mockHttpClientFactory.Object, null!, mockConfiguration.Object, mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ProxyCheckService(mockHttpClientFactory.Object, mockMemoryCache.Object, mockConfiguration.Object, null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns("test-api-key");

        // Act
        var service = new ProxyCheckService(
            mockHttpClientFactory.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task GetIpRiskDataAsync_WithEmptyIPAddress_ReturnsErrorResult()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns("test-api-key");
        var service = new ProxyCheckService(
            mockHttpClientFactory.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var result = await service.GetIpRiskDataAsync(string.Empty);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Invalid IP address", result.ErrorMessage);
    }

    [Fact]
    public async Task GetIpRiskDataAsync_WithNullIPAddress_ReturnsErrorResult()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns("test-api-key");
        var service = new ProxyCheckService(
            mockHttpClientFactory.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var result = await service.GetIpRiskDataAsync(null!);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Invalid IP address", result.ErrorMessage);
    }

    [Fact]
    public async Task GetIpRiskDataAsync_WithCachedResult_ReturnsCachedData()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns("test-api-key");
        var ipAddress = "192.168.1.1";
        var cachedResult = new ProxyCheckResult
        {
            IpAddress = ipAddress,
            IsError = false,
            RiskScore = 50
        };

        object? cacheValue = cachedResult;
        mockMemoryCache
            .Setup(c => c.TryGetValue(It.IsAny<object>(), out cacheValue))
            .Returns(true);

        var service = new ProxyCheckService(
            mockHttpClientFactory.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var result = await service.GetIpRiskDataAsync(ipAddress);

        // Assert
        Assert.False(result.IsError);
        Assert.Equal(ipAddress, result.IpAddress);
        Assert.Equal(50, result.RiskScore);
    }

    [Fact]
    public async Task GetIpRiskDataAsync_WithNoApiKey_ReturnsErrorResult()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns((string?)null);
        object? cacheValue = null;
        mockMemoryCache
            .Setup(c => c.TryGetValue(It.IsAny<object>(), out cacheValue))
            .Returns(false);

        var service = new ProxyCheckService(
            mockHttpClientFactory.Object,
            mockMemoryCache.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var result = await service.GetIpRiskDataAsync("192.168.1.1");

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("API key not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task GetIpRiskDataAsync_WithHttpError_ReturnsErrorResult()
    {
        // Arrange
        mockConfiguration.Setup(c => c["ProxyCheck:ApiKey"]).Returns("test-api-key");
        object? cacheValue = null;
        mockMemoryCache
            .Setup(c => c.TryGetValue(It.IsAny<object>(), out cacheValue))
            .Returns(false);

        var httpResponseMessage = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent("Bad Request")
        };

        try
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(httpResponseMessage);

            using var httpClient = new HttpClient(mockHandler.Object);
            mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var service = new ProxyCheckService(
                mockHttpClientFactory.Object,
                mockMemoryCache.Object,
                mockConfiguration.Object,
                mockLogger.Object);

            // Act
            var result = await service.GetIpRiskDataAsync("192.168.1.1");

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("API Error", result.ErrorMessage);
        }
        finally
        {
            httpResponseMessage.Dispose();
        }
    }

    [Fact]
    public void ProxyCheckResult_GetRiskClass_WithHighRisk_ReturnsCorrectClass()
    {
        // Arrange
        var result = new ProxyCheckResult { RiskScore = 85 };

        // Act
        var riskClass = result.GetRiskClass();

        // Assert
        Assert.Equal("text-bg-danger", riskClass);
    }

    [Fact]
    public void ProxyCheckResult_GetRiskClass_WithMediumRisk_ReturnsCorrectClass()
    {
        // Arrange
        var result = new ProxyCheckResult { RiskScore = 60 };

        // Act
        var riskClass = result.GetRiskClass();

        // Assert
        Assert.Equal("text-bg-warning", riskClass);
    }

    [Fact]
    public void ProxyCheckResult_GetRiskClass_WithLowRisk_ReturnsCorrectClass()
    {
        // Arrange
        var result = new ProxyCheckResult { RiskScore = 30 };

        // Act
        var riskClass = result.GetRiskClass();

        // Assert
        Assert.Equal("text-bg-info", riskClass);
    }

    [Fact]
    public void ProxyCheckResult_GetRiskClass_WithVeryLowRisk_ReturnsCorrectClass()
    {
        // Arrange
        var result = new ProxyCheckResult { RiskScore = 10 };

        // Act
        var riskClass = result.GetRiskClass();

        // Assert
        Assert.Equal("text-bg-success", riskClass);
    }
}
