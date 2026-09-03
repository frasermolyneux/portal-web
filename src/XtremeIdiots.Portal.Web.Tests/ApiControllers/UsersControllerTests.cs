using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Observability.ApplicationInsights.Auditing;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class UsersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<UserManager<IdentityUser>> mockUserManager;
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<UsersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    public UsersControllerTests()
    {
        var mockUserStore = new Mock<IUserStore<IdentityUser>>();
        mockUserManager = new Mock<UserManager<IdentityUser>>(
            mockUserStore.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private UsersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new UsersController(
            mockAuthorizationService.Object,
            mockUserManager.Object,
            mockRepositoryApiClient.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            auditLogger);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        controller.Request.Body = new MemoryStream();
        return controller;
    }

    [Theory]
    [InlineData(GameType.Unknown)]
    [InlineData(GameType.Insurgency)]
    public async Task GetGameModeratorsAjax_UnsupportedGame_ReturnsBadRequestWithoutRepositoryCalls(GameType gameType)
    {
        var sut = CreateSut();

        var result = await sut.GetGameModeratorsAjax(gameType);

        Assert.IsType<BadRequestObjectResult>(result);
        mockRepositoryApiClient.VerifyNoOtherCalls();
        mockAuthorizationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetGameModeratorsAjax_Cod4x_AuthorizesAgainstCanonicalCod4Scope()
    {
        var sut = CreateSut();
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4, AuthPolicies.Users_ManageClaims))
            .ReturnsAsync(AuthorizationResult.Success());

        var result = await sut.GetGameModeratorsAjax(GameType.CallOfDuty4x);

        Assert.IsType<BadRequestObjectResult>(result);
        mockAuthorizationService.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            GameType.CallOfDuty4,
            AuthPolicies.Users_ManageClaims), Times.Once);
        mockRepositoryApiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetGameModeratorsAjax_UnauthorizedGame_ReturnsForbidWithoutRepositoryCalls()
    {
        var sut = CreateSut();
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims))
            .ReturnsAsync(AuthorizationResult.Failed());

        var result = await sut.GetGameModeratorsAjax(GameType.CallOfDuty5);

        Assert.IsType<ForbidResult>(result);
        mockAuthorizationService.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            GameType.CallOfDuty5,
            AuthPolicies.Users_ManageClaims), Times.Once);
        mockRepositoryApiClient.VerifyNoOtherCalls();
    }
}
