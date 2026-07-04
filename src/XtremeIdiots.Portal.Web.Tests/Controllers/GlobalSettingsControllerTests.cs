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
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GlobalSettingsControllerTests
{
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<GlobalSettingsController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;
    private readonly IGlobalSettingsService globalSettingsService = new GlobalSettingsService(
        new NamespaceSettingsParser(),
        new NamespaceSettingsSerializer());

    private GlobalSettingsController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new GlobalSettingsController(
            mockRepositoryApiClient.Object,
            globalSettingsService,
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
                globalSettingsService,
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
                globalSettingsService,
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
                globalSettingsService,
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
    public void PopulateModelFromNamespace_BroadcastsNamespace_MapsGlobalBroadcastDefaults()
    {
        var sut = CreateSut();
        var method = typeof(GlobalSettingsController).GetMethod("PopulateModelFromNamespace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var model = new GlobalSettingsViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = BroadcastSettingsConstants.Namespace,
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "enabled": true,
                            "intervalSeconds": 600,
                            "messages": [
                                { "message": "^1Global welcome", "enabled": true },
                                { "message": "^2Admins online", "enabled": false }
                            ]
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.True(model.BroadcastsEnabled);
        Assert.Equal(600, model.BroadcastsIntervalSeconds);
        Assert.Equal(2, model.BroadcastMessages.Count);
        Assert.Equal("^1Global welcome", model.BroadcastMessages[0].Message);
        Assert.False(model.BroadcastMessages[1].Enabled);
    }

    [Fact]
    public void PopulateModelFromNamespace_ServerListNamespace_MapsGlobalServerListDefaults()
    {
        var sut = CreateSut();
        var method = typeof(GlobalSettingsController).GetMethod("PopulateModelFromNamespace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var model = new GlobalSettingsViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = ServerListSettingsConstants.Namespace,
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "htmlBanner": "<b>Global</b>"
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.Equal("<b>Global</b>", model.ServerListHtmlBanner);
    }

    [Fact]
    public void PopulateModelFromNamespace_ServerListLegacyNamespace_IsIgnored()
    {
        var sut = CreateSut();
        var method = typeof(GlobalSettingsController).GetMethod("PopulateModelFromNamespace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var model = new GlobalSettingsViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "serverList",
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "htmlBanner": "<b>Legacy global</b>"
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.Null(model.ServerListHtmlBanner);
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
        Assert.Equal(ChatCommandSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var defaults = doc.RootElement.GetProperty("defaults");

        Assert.False(defaults.TryGetProperty("requiredTags", out _));
    }

    [Fact]
    public async Task Index_Post_ChatCommandsDefaultsDisabled_PreservesFuMessages()
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
                DefaultsEnabled = false,
                Commands =
                [
                    new ChatCommandGlobalEntryViewModel
                    {
                        Name = "fu",
                        Prefix = "!fu",
                        Usage = "!fu <player name>",
                        Description = "Send a random funny message to a player.",
                        IsMutating = true,
                        Messages =
                        [
                            new BroadcastMessageViewModel
                            {
                                Message = "fu-{name}",
                                Enabled = true
                            }
                        ]
                    }
                ]
            }
        };

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue(ChatCommandSettingsConstants.Namespace, out var chatCommandsJson));

        using var doc = System.Text.Json.JsonDocument.Parse(chatCommandsJson);
        var defaults = doc.RootElement.GetProperty("defaults");
        Assert.False(defaults.GetProperty("enabled").GetBoolean());

        var fu = doc.RootElement
            .GetProperty("commands")
            .GetProperty("fu")
            .GetProperty("settings")
            .GetProperty("messages")[0];

        Assert.Equal("fu-{name}", fu.GetProperty("message").GetString());
        Assert.True(fu.GetProperty("enabled").GetBoolean());

        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.DeleteConfiguration(
                ChatCommandSettingsConstants.Namespace,
                It.IsAny<CancellationToken>()),
            Times.Never);
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
        Assert.Equal(WelcomeMessageSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("defaults").GetProperty("connectionDelaySeconds").GetInt32());
        var firstRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("global-rule", firstRule.GetProperty("id").GetString());
        Assert.Equal("Public", firstRule.GetProperty("visibility").GetString());
        Assert.Equal("vip", firstRule.GetProperty("requiredTags")[0].GetString());
        Assert.Equal("staff", firstRule.GetProperty("requiredTags")[1].GetString());
    }

    [Fact]
    public async Task Index_Post_WelcomeMessagesDisabled_PreservesRulesAndDoesNotDeleteNamespace()
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
                Enabled = false,
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
                        RequiredTagsCsv = "vip",
                        ConnectionDelaySeconds = 3
                    }
                ]
            }
        };

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue(WelcomeMessageSettingsViewModelConstants.Namespace, out var welcomeMessagesJson));

        using var doc = System.Text.Json.JsonDocument.Parse(welcomeMessagesJson);
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
        var firstRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("global-rule", firstRule.GetProperty("id").GetString());
        Assert.Equal("Welcome {name}", firstRule.GetProperty("messageTemplate").GetString());

        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.DeleteConfiguration(
                WelcomeMessageSettingsViewModelConstants.Namespace,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Index_Post_BroadcastsAndServerList_UpsertsGlobalContracts()
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
            BroadcastsEnabled = false,
            BroadcastsIntervalSeconds = 900,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = "^1Global welcome",
                    Enabled = true
                }
            ],
            ServerListHtmlBanner = "<b>Global banner</b>"
        };

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue(BroadcastSettingsConstants.Namespace, out var broadcastsJson));
        using var broadcastsDoc = System.Text.Json.JsonDocument.Parse(broadcastsJson);
        Assert.Equal(BroadcastSettingsConstants.SchemaVersion, broadcastsDoc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(broadcastsDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(900, broadcastsDoc.RootElement.GetProperty("intervalSeconds").GetInt32());
        Assert.Equal("^1Global welcome", broadcastsDoc.RootElement.GetProperty("messages")[0].GetProperty("message").GetString());

        Assert.True(upsertPayloads.TryGetValue(ServerListSettingsConstants.Namespace, out var serverListJson));
        using var serverListDoc = System.Text.Json.JsonDocument.Parse(serverListJson);
        Assert.Equal(ServerListSettingsConstants.SchemaVersion, serverListDoc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("<b>Global banner</b>", serverListDoc.RootElement.GetProperty("htmlBanner").GetString());
    }

    [Fact]
    public async Task Index_Post_Cod4xCommands_UpsertsModifiedMinPowerValues()
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
            Cod4xCommandsEnabled = true
        };

        var kickCommand = model.Cod4xCommands.Single(static command => string.Equals(command.Name, "kick", StringComparison.OrdinalIgnoreCase));
        kickCommand.Enabled = false;
        kickCommand.MinPower = 99;

        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue(Cod4xCommandSettingsConstants.Namespace, out var cod4xCommandsJson));

        using var doc = System.Text.Json.JsonDocument.Parse(cod4xCommandsJson);
        Assert.Equal(Cod4xCommandSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());

        var kickSettings = doc.RootElement.GetProperty("commands").GetProperty("kick");
        Assert.False(kickSettings.GetProperty("enabled").GetBoolean());
        Assert.Equal(99, kickSettings.GetProperty("minPower").GetInt32());
    }

    [Fact]
    public async Task Index_Post_InvalidBroadcastMessage_DoesNotCallUpsert()
    {
        var sut = CreateSut();
        sut.ModelState.AddModelError("BroadcastMessages[0].Message", "Broadcast message is required.");

        var model = new GlobalSettingsViewModel
        {
            BroadcastsEnabled = true,
            BroadcastsIntervalSeconds = 600,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel
                {
                    Message = "",
                    Enabled = true
                }
            ]
        };

        var result = await sut.Index(model);

        Assert.IsType<ViewResult>(result);
        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.UpsertConfiguration(
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
