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
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Interfaces.V1;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Ftp;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
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
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        var listing = new FtpDirectoryListingDto("/maps", null, []);

        fileBrowseApi
            .Setup(x => x.BrowseDirectory(gameServerId, "/maps", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FtpDirectoryListingDto>(HttpStatusCode.OK, new ApiResponse<FtpDirectoryListingDto>(listing)));

        var sut = CreateSut();

        // Act
        var result = await sut.Browse(gameServerId, "game-server-configuration", "/maps");

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
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        fileBrowseApi
            .Setup(x => x.BrowseDirectory(gameServerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FtpDirectoryListingDto>(HttpStatusCode.BadRequest, new ApiResponse<FtpDirectoryListingDto>(new ApiError("TEST_ERROR", "Test error message"))));

        var sut = CreateSut();

        // Act
        var result = await sut.Browse(gameServerId, "game-server-configuration");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
        Assert.NotNull(objectResult.Value);
    }

    [Theory]
    [InlineData("game-server-configuration", AuthPolicies.GameServers_Write)]
    [InlineData("file-transport-configuration", AuthPolicies.GameServers_Write)]
    [InlineData("screenshot-configuration", AuthPolicies.GameServers_Write)]
    public async Task Browse_WhenPurposeRequiresGameServerPolicy_AuthorizesAgainstResolvedGameType(
        string purpose,
        string expectedPolicy)
    {
        var gameServerId = Guid.NewGuid();
        var gameType = GameType.CallOfDuty4;
        SetupGameServer(gameServerId, gameType);
        authorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());
        SetupBrowse(gameServerId);

        var result = await CreateSut().Browse(gameServerId, purpose);

        Assert.IsType<OkObjectResult>(result);
        authorizationService.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, expectedPolicy), Times.Once);
    }

    [Theory]
    [InlineData("file-transport-configuration", AuthPolicies.GameServers_Credentials_FileTransport_Write)]
    [InlineData("screenshot-configuration", AuthPolicies.GameServers_Admin_Screenshots_Configure)]
    public async Task Browse_WhenPurposeRequiresAdditionalPolicy_RequiresEveryPolicy(
        string purpose,
        string requiredPolicy)
    {
        var gameServerId = Guid.NewGuid();
        var gameType = GameType.CallOfDuty4;
        SetupGameServer(gameServerId, gameType);
        authorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());
        authorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, requiredPolicy))
            .ReturnsAsync(AuthorizationResult.Failed());

        var result = await CreateSut().Browse(gameServerId, purpose);

        Assert.IsType<ForbidResult>(result);
        authorizationService.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), gameType, requiredPolicy), Times.Once);
        fileBrowseApi.Verify(x => x.BrowseDirectory(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Browse_WhenMapRotationAssignment_UsesConcreteServerResource()
    {
        var gameServerId = Guid.NewGuid();
        var gameType = GameType.CallOfDuty4;
        var resource = (gameType, gameServerId);
        SetupGameServer(gameServerId, gameType);
        authorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                resource,
                AuthPolicies.MapRotations_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());
        SetupBrowse(gameServerId);

        var result = await CreateSut().Browse(gameServerId, "map-rotation-assignment", "/configs");

        Assert.IsType<OkObjectResult>(result);
        authorizationService.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            resource,
            AuthPolicies.MapRotations_Deploy), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("credential-write")]
    public async Task Browse_WhenPurposeIsNotSupported_FailsBeforeServerLookup(string? purpose)
    {
        var result = await CreateSut().Browse(Guid.NewGuid(), purpose);

        Assert.IsType<BadRequestObjectResult>(result);
        repositoryApiClient.Verify(x => x.GameServers.V1.GetGameServer(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fileBrowseApi.Verify(x => x.BrowseDirectory(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Browse_WhenFileTransportIsUnavailable_DoesNotBrowse()
    {
        var gameServerId = Guid.NewGuid();
        var gameServer = CreateGameServer(gameServerId, GameType.CallOfDuty4, "Server Alpha");
        SetProperty(gameServer, nameof(GameServerDto.FileTransportEnabled), false);
        repositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));

        var result = await CreateSut().Browse(gameServerId, "game-server-configuration");

        Assert.IsType<BadRequestObjectResult>(result);
        authorizationService.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()), Times.Never);
        fileBrowseApi.Verify(x => x.BrowseDirectory(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupGameServer(Guid gameServerId, GameType gameType)
    {
        repositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(gameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(
                HttpStatusCode.OK,
                new ApiResponse<GameServerDto>(CreateGameServer(gameServerId, gameType, "Server Alpha"))));
    }

    private void SetupBrowse(Guid gameServerId)
    {
        fileBrowseApi
            .Setup(x => x.BrowseDirectory(gameServerId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FtpDirectoryListingDto>(
                HttpStatusCode.OK,
                new ApiResponse<FtpDirectoryListingDto>(new FtpDirectoryListingDto("/", null, []))));
    }

    private static GameServerDto CreateGameServer(Guid gameServerId, GameType gameType, string title)
    {
        var gameServer = new GameServerDto();
        SetProperty(gameServer, nameof(GameServerDto.GameServerId), gameServerId);
        SetProperty(gameServer, nameof(GameServerDto.GameType), gameType);
        SetProperty(gameServer, nameof(GameServerDto.Title), title);
        SetProperty(gameServer, nameof(GameServerDto.FileTransportEnabled), true);
        SetProperty(gameServer, nameof(GameServerDto.FileTransportType), FileTransportType.Ftp);
        return gameServer;
    }

    private static void SetProperty<T>(GameServerDto gameServer, string propertyName, T value)
    {
        var property = typeof(GameServerDto).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property.SetValue(gameServer, value);
    }
}
