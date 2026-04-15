using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class DemosControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<DemosController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private DemosController CreateSut(Dictionary<string, StringValues>? headers = null)
    {
        var mockUserStore = new Mock<IUserStore<IdentityUser>>();
        var mockUserManager = new Mock<UserManager<IdentityUser>>(
            mockUserStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var mockSignInManager = new Mock<SignInManager<IdentityUser>>(
            mockUserManager.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<IdentityUser>>().Object,
            null!, null!, null!, null!);

        var controller = new DemosController(
            mockAuthorizationService.Object,
            mockUserManager.Object,
            mockSignInManager.Object,
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext();
        if (headers != null)
        {
            foreach (var header in headers)
                httpContext.Request.Headers[header.Key] = header.Value;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    private void SetupUserProfileApiMock(ApiResult<UserProfileDto> result)
    {
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByDemoAuthKey(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    [Fact]
    public async Task WhoAmI_WithNoAuthKeyHeader_ReturnsUnauthorized()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.WhoAmI();

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task WhoAmI_WithEmptyAuthKey_ReturnsUnauthorized()
    {
        // Arrange
        var sut = CreateSut(new Dictionary<string, StringValues>
        {
            { "demo-manager-auth-key", "" }
        });

        // Act
        var result = await sut.WhoAmI();

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task WhoAmI_WithInvalidAuthKey_ReturnsUnauthorized()
    {
        // Arrange
        SetupUserProfileApiMock(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));

        var sut = CreateSut(new Dictionary<string, StringValues>
        {
            { "demo-manager-auth-key", "invalid-key" }
        });

        // Act
        var result = await sut.WhoAmI();

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task WhoAmI_WithValidAuthKey_ReturnsOkWithUserInfo()
    {
        // Arrange
        var userProfile = CreateTestUserProfile("TestPlayer", "forum-12345");
        var apiResult = new ApiResult<UserProfileDto>(
            HttpStatusCode.OK,
            new ApiResponse<UserProfileDto>(userProfile));

        SetupUserProfileApiMock(apiResult);

        var sut = CreateSut(new Dictionary<string, StringValues>
        {
            { "demo-manager-auth-key", "valid-key-123" }
        });

        // Act
        var result = await sut.WhoAmI();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        var response = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal("forum-12345", response.RootElement.GetProperty("userId").GetString());
        Assert.Equal("TestPlayer", response.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task WhoAmI_WithValidAuthKey_ReturnsCorrectDisplayName()
    {
        // Arrange
        var userProfile = CreateTestUserProfile("AnotherUser", "forum-99999");
        var apiResult = new ApiResult<UserProfileDto>(
            HttpStatusCode.OK,
            new ApiResponse<UserProfileDto>(userProfile));

        SetupUserProfileApiMock(apiResult);

        var sut = CreateSut(new Dictionary<string, StringValues>
        {
            { "demo-manager-auth-key", "another-valid-key" }
        });

        // Act
        var result = await sut.WhoAmI();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        var response = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal("AnotherUser", response.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("forum-99999", response.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task WhoAmI_WhenApiThrows_ReturnsStatusCode500()
    {
        // Arrange
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByDemoAuthKey(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API unavailable"));

        var sut = CreateSut(new Dictionary<string, StringValues>
        {
            { "demo-manager-auth-key", "valid-key" }
        });

        // Act
        var result = await sut.WhoAmI();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    private static UserProfileDto CreateTestUserProfile(string displayName, string forumId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            UserProfileId = Guid.NewGuid(),
            XtremeIdiotsForumId = forumId,
            DemoAuthKey = "test-key",
            DisplayName = displayName,
            UserProfileClaims = Array.Empty<object>()
        });

        return JsonConvert.DeserializeObject<UserProfileDto>(json)!;
    }
}
