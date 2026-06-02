using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Observability.ApplicationInsights.Auditing;
using MX.Api.Abstractions;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GameServersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<GameServersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private GameServersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new GameServersController(
            mockAuthorizationService.Object,
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
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

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
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                null!,
                mockLogger.Object,
                mockConfiguration.Object,
                auditLogger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                telemetryClient,
                null!,
                mockConfiguration.Object,
                auditLogger));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameServersController(
                mockAuthorizationService.Object,
                mockRepositoryApiClient.Object,
                telemetryClient,
                mockLogger.Object,
                null!,
                auditLogger));
    }

    [Fact]
    public void PopulateConfigFromNamespace_BroadcastsNamespace_MapsAllFields()
    {
        // Arrange
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "broadcasts",
            Configuration = """
            {
              "enabled": true,
              "intervalSeconds": 700,
              "messages": [
                { "message": "^1Welcome to XI", "enabled": true },
                { "message": "^2Admins online", "enabled": false }
              ]
            }
            """
        }));

        // Act
        method.Invoke(sut, [model, config]);

        // Assert
        Assert.True(model.BroadcastsEnabled);
        Assert.Equal(700, model.BroadcastsIntervalSeconds);
        Assert.Equal(2, model.BroadcastMessages.Count);
        Assert.Equal("^1Welcome to XI", model.BroadcastMessages[0].Message);
        Assert.True(model.BroadcastMessages[0].Enabled);
        Assert.Equal("^2Admins online", model.BroadcastMessages[1].Message);
        Assert.False(model.BroadcastMessages[1].Enabled);
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_UpsertsBroadcastsContract()
    {
        // Arrange
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("SaveConfigNamespacesAsync");
        var gameServerId = Guid.NewGuid();
        var upsertPayloads = new Dictionary<string, string>();

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, UpsertConfigurationDto dto, CancellationToken _) =>
            {
                upsertPayloads[ns] = dto.Configuration;
                var responseDto = JsonConvert.DeserializeObject<ConfigurationDto>("{}");
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(responseDto));
            });

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = gameServerId,
                Title = "Server Alpha",
                AgentEnabled = true
            },
            BroadcastsEnabled = true,
            BroadcastsIntervalSeconds = null,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel { Message = "^1Welcome", Enabled = true },
                new BroadcastMessageViewModel { Message = "^2Rules", Enabled = false }
            ]
        };

        // Act
        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        // Assert
        Assert.True(upsertPayloads.TryGetValue("broadcasts", out var broadcastsJson));
        using var doc = System.Text.Json.JsonDocument.Parse(broadcastsJson);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal(500, root.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
        Assert.False(root.GetProperty("messages")[1].GetProperty("enabled").GetBoolean());
        Assert.Equal("^2Rules", root.GetProperty("messages")[1].GetProperty("message").GetString());
    }

    private static MethodInfo GetPrivateInstanceMethod(string name)
    {
        var method = typeof(GameServersController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method;
    }

    [Fact]
    public async Task Edit_WhenUserCannotEditFileTransport_PreservesExistingFileTransportValues()
    {
        // Arrange
        var existingServer = CreateGameServerDto(ftpEnabled: true, fileTransportEnabled: true, fileTransportType: "Ftp");
        var updateResultDto = JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = existingServer.GameServerId,
            Title = existingServer.Title,
            GameType = existingServer.GameType,
            Hostname = existingServer.Hostname,
            QueryPort = existingServer.QueryPort,
            AgentEnabled = existingServer.AgentEnabled,
            FtpEnabled = existingServer.FtpEnabled,
            RconEnabled = existingServer.RconEnabled,
            BanFileSyncEnabled = existingServer.BanFileSyncEnabled,
            BanFileRootPath = existingServer.BanFileRootPath,
            ServerListEnabled = existingServer.ServerListEnabled,
            ServerListPosition = existingServer.ServerListPosition
        }));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        var existingSftpConfig = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "sftp",
            Configuration = "{\"hostname\":\"sftp.example.com\",\"port\":22,\"username\":\"test-user\",\"password\":\"test-pass\"}",
            LastModifiedUtc = DateTime.UtcNow
        }))!;

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.GetConfigurations(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([existingSftpConfig]))));

        EditGameServerDto? capturedUpdate = null;
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()))
            .Callback<EditGameServerDto, CancellationToken>((dto, _) => capturedUpdate = dto)
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(updateResultDto)));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = true,
                FileTransportType = FileTransportType.Sftp,
                RconEnabled = existingServer.RconEnabled,
                BanFileSyncEnabled = existingServer.BanFileSyncEnabled,
                BanFileRootPath = existingServer.BanFileRootPath,
                ServerListEnabled = existingServer.ServerListEnabled
            }
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Edit(model, CancellationToken.None);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(capturedUpdate);
        Assert.True(capturedUpdate.FileTransportEnabled);
        Assert.Equal(XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType.Ftp, capturedUpdate.FileTransportType);
    }

    [Fact]
    public async Task Edit_WhenUserSelectsSftp_PersistsTransportEnabledAndType()
    {
        // Arrange
        var existingServer = CreateGameServerDto(ftpEnabled: true, fileTransportEnabled: true, fileTransportType: "Ftp");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        EditGameServerDto? capturedUpdate = null;
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()))
            .Callback<EditGameServerDto, CancellationToken>((dto, _) => capturedUpdate = dto)
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = false,
                FileTransportEnabled = true,
                FileTransportType = FileTransportType.Sftp,
                RconEnabled = false,
                BanFileSyncEnabled = false,
                BanFileRootPath = "/",
                ServerListEnabled = false
            },
            FileTransportConfigHostname = "sftp.example.com",
            FileTransportConfigPort = 22,
            FileTransportConfigUsername = "test-user",
            FileTransportConfigPassword = "test-pass",
            FileTransportConfigHostKeyFingerprint = "40:44:78:e0:7a:e0:c2:e7:fe:37:14:9e:4f:09:e0:07"
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Edit(model, CancellationToken.None);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(capturedUpdate);
        Assert.True(capturedUpdate.FileTransportEnabled);
        Assert.Equal(XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType.Sftp, capturedUpdate.FileTransportType);
        Assert.Null(capturedUpdate.FtpEnabled);
    }

    private static GameServerDto CreateGameServerDto(bool ftpEnabled, bool fileTransportEnabled, string fileTransportType)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "Test Server",
            GameType = GameType.CallOfDuty4,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = false,
            FileTransportEnabled = fileTransportEnabled,
            FileTransportType = fileTransportType,
            FtpEnabled = ftpEnabled,
            RconEnabled = false,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }
}
