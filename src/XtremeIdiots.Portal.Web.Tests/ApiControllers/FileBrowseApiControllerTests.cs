using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using System.Security.Claims;
using System.Reflection;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Ftp;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Interfaces.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.ApiControllers;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class FileBrowseApiControllerTests
{
    private readonly Mock<IAuthorizationService> authorizationService = new();
    private readonly Mock<IRepositoryApiClient> repositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IServersApiClient> serversApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IVersionedFileBrowseApi> versionedFileBrowseApi = new();
    private readonly Mock<IFileBrowseApi> fileBrowseApi = new();
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<FileBrowseApiController>> logger = new();
    private readonly Mock<IConfiguration> configuration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    public FileBrowseApiControllerTests()
    {
        versionedFileBrowseApi.SetupGet(x => x.V1).Returns(fileBrowseApi.Object);
        serversApiClient.SetupGet(x => x.FileBrowse).Returns(versionedFileBrowseApi.Object);
    }

    private FileBrowseApiController CreateSut(ClaimsPrincipal? user = null)
    {
        return new FileBrowseApiController(
            authorizationService.Object,
            repositoryApiClient.Object,
            serversApiClient.Object,
            telemetryClient,
            logger.Object,
            configuration.Object,
            auditLogger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth")) }
            }
        };
    }

    [Fact]
    public async Task Browse_WhenAuthorized_UsesFileBrowseSurfaceAndReturnsListing()
    {
        // Arrange
        var gameServerId = Guid.NewGuid();
        var gameType = GameType.CallOfDuty4;
        var gameServer = CreateGameServer(gameServerId, gameType, "Server Alpha");

        repositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));

        authorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var listing = new FtpDirectoryListingDto("/maps", null, []);

        fileBrowseApi
            .Setup(x => x.BrowseDirectory(gameServerId, "/maps", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FtpDirectoryListingDto>(HttpStatusCode.OK, new ApiResponse<FtpDirectoryListingDto>(listing)));

        var sut = CreateSut();

        // Act
        var result = await sut.Browse(gameServerId, "/maps");

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<FtpDirectoryListingDto>(ok.Value);
        Assert.Equal("/maps", returned.CurrentPath);
        fileBrowseApi.Verify(x => x.BrowseDirectory(gameServerId, "/maps", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Browse_WhenDownstreamFails_ReturnsStatusCodeWithErrorResponse()
    {
        // Arrange
        var gameServerId = Guid.NewGuid();
        var gameType = GameType.CallOfDuty4;
        var gameServer = CreateGameServer(gameServerId, gameType, "Server Alpha");

        repositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));

        authorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        fileBrowseApi
            .Setup(x => x.BrowseDirectory(gameServerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FtpDirectoryListingDto>(HttpStatusCode.BadRequest, new ApiResponse<FtpDirectoryListingDto>(new ApiError("TEST_ERROR", "Test error message"))));

        var sut = CreateSut();

        // Act
        var result = await sut.Browse(gameServerId);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
    }

    private static GameServerDto CreateGameServer(Guid gameServerId, GameType gameType, string title)
    {
        var gameServer = new GameServerDto();
        SetProperty(gameServer, nameof(GameServerDto.GameServerId), gameServerId);
        SetProperty(gameServer, nameof(GameServerDto.GameType), gameType);
        SetProperty(gameServer, nameof(GameServerDto.Title), title);
        return gameServer;
    }

    private static void SetProperty<T>(GameServerDto gameServer, string propertyName, T value)
    {
        var property = typeof(GameServerDto).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property.SetValue(gameServer, value);
    }
}