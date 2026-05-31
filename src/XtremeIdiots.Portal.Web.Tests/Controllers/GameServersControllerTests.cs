using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Observability.ApplicationInsights.Auditing;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
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
}
