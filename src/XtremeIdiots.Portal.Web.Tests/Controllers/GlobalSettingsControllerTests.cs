using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Server.Events.Processor.App.Commands;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GlobalSettingsControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<GlobalSettingsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;

    private GlobalSettingsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new GlobalSettingsController(
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
        var sut = CreateSut();
        Assert.NotNull(sut);
    }

    [Fact]
    public void Constructor_WithNullTelemetryClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GlobalSettingsController(
                mockRepositoryApiClient.Object,
                null!,
                mockLogger.Object,
                mockConfiguration.Object,
                auditLogger));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GlobalSettingsController(
                mockRepositoryApiClient.Object,
                telemetryClient,
                null!,
                mockConfiguration.Object,
                auditLogger));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new GlobalSettingsController(
                mockRepositoryApiClient.Object,
                telemetryClient,
                mockLogger.Object,
                null!,
                auditLogger));
    }

    [Fact]
    public void PopulateModelFromNamespace_ChatCommandsNamespace_MapsDefaultsAndFuMessages()
    {
        var sut = CreateSut();
        var method = typeof(GlobalSettingsController).GetMethod("PopulateModelFromNamespace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var model = new GlobalSettingsViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "chatCommands",
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "defaults": {
                                "enabled": true,
                                "freshnessSeconds": { "default": 8, "readOnly": 6, "mutating": 4 },
                                "requiredTags": ["tag-a"]
                            },
                            "commands": {
                                "fu": {
                                    "enabled": true,
                                    "freshnessSeconds": 9,
                                    "requiredTags": ["tag-fu"],
                                    "settings": {
                                        "messages": [
                                            { "message": "fu-{name}", "enabled": true }
                                        ]
                                    }
                                }
                            }
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.True(model.ChatCommands.DefaultsEnabled);
        Assert.Equal(8, model.ChatCommands.DefaultFreshnessSeconds);
        Assert.Equal(6, model.ChatCommands.ReadOnlyFreshnessSeconds);
        Assert.Equal(4, model.ChatCommands.MutatingFreshnessSeconds);
        Assert.Equal("tag-a", model.ChatCommands.DefaultRequiredTags);

        var fu = model.ChatCommands.Commands.Single(x => x.Name == "fu");
        Assert.True(fu.Enabled);
        Assert.Equal(9, fu.FreshnessSeconds);
        Assert.Equal("tag-fu", fu.RequiredTags);
        Assert.Single(fu.Messages);
        Assert.Equal("fu-{name}", fu.Messages[0].Message);
        Assert.True(fu.Messages[0].Enabled);

        var commands = model.ChatCommands.Commands.Single(x => x.Name == "commands");
        Assert.Equal(["!help"], commands.Aliases);
    }

    [Fact]
    public async Task Index_Post_ChatCommandsBlankRequirements_OmitsGlobalTagDefaults()
    {
        var sut = CreateSut();
        var upsertPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        mockRepositoryApiClient
            .Setup(x => x.GlobalConfigurations.V1.UpsertConfiguration(
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ns, UpsertConfigurationDto dto, CancellationToken _) =>
            {
                upsertPayloads[ns] = dto.Configuration;
                var responseDto = JsonConvert.DeserializeObject<ConfigurationDto>("{}");
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(responseDto));
            });

        var model = new GlobalSettingsViewModel
        {
            ChatCommands = new ChatCommandGlobalSettingsViewModel
            {
                DefaultRequiredTags = string.Empty
            }
        };

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue("chatCommands", out var chatCommandsJson));

        using var doc = System.Text.Json.JsonDocument.Parse(chatCommandsJson);
        var defaults = doc.RootElement.GetProperty("defaults");

        Assert.False(defaults.TryGetProperty("requiredTags", out _));
    }

    [Fact]
    public async Task Index_Post_WelcomeMessagesRules_UpsertsWelcomeMessageContract()
    {
        var sut = CreateSut();
        var upsertPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        mockRepositoryApiClient
            .Setup(x => x.GlobalConfigurations.V1.UpsertConfiguration(
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ns, UpsertConfigurationDto dto, CancellationToken _) =>
            {
                upsertPayloads[ns] = dto.Configuration;
                var responseDto = JsonConvert.DeserializeObject<ConfigurationDto>("{}");
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(responseDto));
            });

        var model = new GlobalSettingsViewModel
        {
            WelcomeMessages = new WelcomeMessageGlobalSettingsViewModel
            {
                Enabled = true,
                CountryFallback = "Unknown",
                DefaultConnectionDelaySeconds = 5,
                StaleThresholdSeconds = 180,
                Rules =
                [
                    new WelcomeMessageRuleEntryViewModel
                    {
                        Id = "global-rule",
                        Enabled = true,
                        Priority = 10,
                        Visibility = WelcomeMessageVisibility.Public,
                        MessageTemplate = "Welcome {name}",
                        RequiredTagsCsv = "vip, staff",
                        ConnectionDelaySeconds = 3
                    }
                ]
            }
        };

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue("welcomeMessages", out var welcomeMessagesJson));

        using var doc = System.Text.Json.JsonDocument.Parse(welcomeMessagesJson);
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("defaults").GetProperty("connectionDelaySeconds").GetInt32());
        var firstRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("global-rule", firstRule.GetProperty("id").GetString());
        Assert.Equal("Public", firstRule.GetProperty("visibility").GetString());
        Assert.Equal("vip", firstRule.GetProperty("requiredTags")[0].GetString());
        Assert.Equal("staff", firstRule.GetProperty("requiredTags")[1].GetString());
    }
}
