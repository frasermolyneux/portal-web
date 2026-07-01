using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Agent;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.BanFiles;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;
using RepositoryFileTransportType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.FileTransportType;
using RepositoryGameServerPlatform = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameServerPlatform;
using RepositoryGameType = XtremeIdiots.Portal.Repository.Abstractions.Constants.V1.GameType;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GameServersControllerTests
{
    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<GameServersController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly IAuditLogger auditLogger = new Mock<IAuditLogger>().Object;
    private readonly IGameServerSettingsService gameServerSettingsService = new GameServerSettingsService(
        new NamespaceSettingsParser(),
        new NamespaceSettingsSerializer());

    private GameServersController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new GameServersController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            gameServerSettingsService,
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
                gameServerSettingsService,
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
                gameServerSettingsService,
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
                gameServerSettingsService,
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
            Configuration = /*lang=json,strict*/ """
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
    public void PopulateConfigFromNamespace_BroadcastsNamespace_ParsesStringBooleans()
    {
        // Arrange
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "broadcasts",
            Configuration = /*lang=json,strict*/ """
            {
              "enabled": "true",
              "intervalSeconds": 600,
              "messages": [
                { "message": "^1Message A", "enabled": "true" },
                { "message": "^2Message B", "enabled": "false" }
              ]
            }
            """
        }));

        // Act
        method.Invoke(sut, [model, config]);

        // Assert
        Assert.True(model.BroadcastsEnabled);
        Assert.Equal(600, model.BroadcastsIntervalSeconds);
        Assert.Equal(2, model.BroadcastMessages.Count);
        Assert.True(model.BroadcastMessages[0].Enabled);
        Assert.False(model.BroadcastMessages[1].Enabled);
    }

    [Fact]
    public void PopulateConfigFromNamespace_ChatCommandsNamespace_MapsServerOverrides()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "chatCommands",
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "commands": {
                                "fu": {
                                    "enabled": false,
                                    "freshnessSeconds": 4,
                                    "requiredTags": ["tag-fu"],
                                    "settings": {
                                        "messages": [
                                            { "message": "server-fu-{name}", "enabled": true }
                                        ]
                                    }
                                }
                            }
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        var fu = model.ChatCommands.Commands.Single(x => x.Name == "fu");
        Assert.True(fu.OverrideEnabled);
        Assert.True(fu.OverrideFreshness);
        Assert.True(fu.OverrideRequiredTags);
        Assert.True(fu.OverrideMessages);
        Assert.False(fu.EnabledOverride.Value);
        Assert.Equal(4, fu.FreshnessSeconds);
        Assert.Equal("tag-fu", fu.RequiredTags);
        Assert.Single(fu.Messages);
        Assert.Equal("server-fu-{name}", fu.Messages[0].Message);

        var commands = model.ChatCommands.Commands.Single(x => x.Name == "commands");
        Assert.Equal(["!help"], commands.Aliases);
    }

    [Fact]
    public void PopulateGlobalDefaults_ChatCommandsNamespace_MapsGlobalCommandDefaults()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateGlobalDefaults");
        var model = new GameServerEditViewModel();
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
                                    "settings": {
                                        "messages": [
                                            { "message": "global-fu-{name}", "enabled": true }
                                        ]
                                    }
                                }
                            }
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.Equal(8, model.GlobalChatCommands.DefaultFreshnessSeconds);
        Assert.Equal(6, model.GlobalChatCommands.ReadOnlyFreshnessSeconds);
        Assert.Equal(4, model.GlobalChatCommands.MutatingFreshnessSeconds);
        var fu = model.GlobalChatCommands.Commands.Single(x => x.Name == "fu");
        Assert.True(fu.Enabled);
        Assert.Single(fu.Messages);
        Assert.Equal("global-fu-{name}", fu.Messages[0].Message);
    }

    [Fact]
    public void PopulateGlobalDefaults_BroadcastsNamespace_MapsGlobalBroadcastDefaults()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateGlobalDefaults");
        var model = new GameServerEditViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = BroadcastSettingsConstants.Namespace,
            Configuration = /*lang=json,strict*/ """
                        {
                            "schemaVersion": 1,
                            "enabled": true,
                            "intervalSeconds": 750,
                            "messages": [
                                { "message": "^1Global welcome", "enabled": true }
                            ]
                        }
                        """
        }));

        method.Invoke(sut, [model, config]);

        Assert.True(model.GlobalBroadcastsEnabled);
        Assert.Equal(750, model.GlobalBroadcastsIntervalSeconds);
        Assert.Single(model.GlobalBroadcastMessages);
        Assert.Equal("^1Global welcome", model.GlobalBroadcastMessages[0].Message);
        Assert.Equal(model.GlobalBroadcastMessages, model.GlobalFunnyMessages);
    }

    [Fact]
    public void PopulateGlobalDefaults_ServerListNamespace_MapsGlobalServerListDefaults()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateGlobalDefaults");
        var model = new GameServerEditViewModel();
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

        Assert.Equal("<b>Global</b>", model.GlobalServerListHtmlBanner);
    }

    [Fact]
    public void PopulateGlobalDefaults_ServerListLegacyNamespace_IsIgnored()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateGlobalDefaults");
        var model = new GameServerEditViewModel();
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

        Assert.Null(model.GlobalServerListHtmlBanner);
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_UpsertsChatCommandsContract()
    {
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
            ChatCommands = new ChatCommandServerSettingsViewModel
            {
                Commands =
                        [
                                new ChatCommandServerEntryViewModel
                                        {
                                                Name = "fu",
                                                Prefix = "!fu",
                                                Usage = "!fu <player name>",
                                                Enabled = false,
                                                OverrideFreshness = true,
                                                FreshnessSeconds = 4,
                                                OverrideRequiredTags = true,
                                                RequiredTags = "tag-fu",
                                                OverrideMessages = true,
                                                Messages =
                                                [
                                                        new BroadcastMessageViewModel { Message = "server-fu-{name}", Enabled = true }
                                                ]
                                        }
                        ]
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.True(upsertPayloads.TryGetValue("chatCommands", out var chatCommandsJson));
        using var doc = System.Text.Json.JsonDocument.Parse(chatCommandsJson);
        Assert.Equal(ChatCommandSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var fu = doc.RootElement.GetProperty("commands").GetProperty("fu");

        Assert.False(fu.GetProperty("enabled").GetBoolean());
        Assert.Equal(4, fu.GetProperty("freshnessSeconds").GetInt32());
        Assert.Equal("tag-fu", fu.GetProperty("requiredTags")[0].GetString());
        Assert.Equal("server-fu-{name}", fu.GetProperty("settings").GetProperty("messages")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_BlankChatCommandRequirements_UpsertsEmptyArrays()
    {
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
            ChatCommands = new ChatCommandServerSettingsViewModel
            {
                Commands =
                        [
                                new ChatCommandServerEntryViewModel
                                        {
                                                Name = "fu",
                                                Prefix = "!fu",
                                                Usage = "!fu <player name>",
                                                Enabled = false,
                                                OverrideFreshness = true,
                                                FreshnessSeconds = 4,
                                                OverrideRequiredTags = true,
                                                RequiredTags = string.Empty,
                                                OverrideMessages = true,
                                                Messages =
                                                [
                                                        new BroadcastMessageViewModel { Message = "server-fu-{name}", Enabled = true }
                                                ]
                                        }
                        ]
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.True(upsertPayloads.TryGetValue("chatCommands", out var chatCommandsJson));
        using var doc = System.Text.Json.JsonDocument.Parse(chatCommandsJson);
        Assert.Equal(ChatCommandSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var fu = doc.RootElement.GetProperty("commands").GetProperty("fu");

        Assert.False(fu.GetProperty("enabled").GetBoolean());
        Assert.Equal(4, fu.GetProperty("freshnessSeconds").GetInt32());
        Assert.Empty(fu.GetProperty("requiredTags").EnumerateArray());
        Assert.Equal("server-fu-{name}", fu.GetProperty("settings").GetProperty("messages")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_InheritModes_OmitsCommandOverrides()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("SaveConfigNamespacesAsync");
        var gameServerId = Guid.NewGuid();
        var upsertPayloads = new Dictionary<string, string>();
        var deletedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.DeleteConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, CancellationToken _) =>
            {
                deletedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = gameServerId,
                Title = "Server Alpha",
                AgentEnabled = true
            },
            ChatCommands = new ChatCommandServerSettingsViewModel
            {
                Commands =
                        [
                                new ChatCommandServerEntryViewModel
                                        {
                                                Name = "fu",
                                                Prefix = "!fu",
                                                Usage = "!fu <player name>",
                                            Enabled = null,
                                                OverrideFreshness = false,
                                                FreshnessSeconds = 99,
                                                OverrideRequiredTags = false,
                                                RequiredTags = "tag-fu",
                                                OverrideMessages = false,
                                                Messages =
                                                [
                                                        new BroadcastMessageViewModel { Message = "server-fu-{name}", Enabled = false }
                                                ]
                                        }
                        ]
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.False(upsertPayloads.ContainsKey(ChatCommandSettingsConstants.Namespace));
        Assert.Contains(ChatCommandSettingsConstants.Namespace, deletedNamespaces);
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_UpsertsWelcomeMessagesContract()
    {
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
            WelcomeMessages = new WelcomeMessageServerSettingsViewModel
            {
                Enabled = true,
                InheritGlobalRules = true,
                CountryFallback = "Unknown",
                DefaultConnectionDelaySeconds = 4,
                StaleThresholdSeconds = 180,
                LocalRules =
                [
                    new WelcomeMessageRuleEntryViewModel
                    {
                        Id = "server-rule",
                        Enabled = true,
                        Priority = 100,
                        Visibility = WelcomeMessageVisibility.Private,
                        MessageTemplate = "Welcome local {name}",
                        RequiredTagsCsv = "vip",
                        ConnectionDelaySeconds = 2
                    }
                ],
                RuleOverrides =
                [
                    new WelcomeMessageRuleOverrideEntryViewModel
                    {
                        Id = "global-rule",
                        Enabled = false,
                        Priority = 90,
                        Visibility = WelcomeMessageVisibility.Public,
                        MessageTemplate = "Overridden template",
                        OverrideRequiredTags = true,
                        RequiredTagsCsv = string.Empty,
                        ConnectionDelaySeconds = 6
                    }
                ]
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.True(upsertPayloads.TryGetValue("welcomeMessages", out var welcomeMessagesJson));
        using var doc = System.Text.Json.JsonDocument.Parse(welcomeMessagesJson);
        Assert.Equal(WelcomeMessageSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("inheritGlobalRules").GetBoolean());

        var localRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("server-rule", localRule.GetProperty("id").GetString());
        Assert.Equal("vip", localRule.GetProperty("requiredTags")[0].GetString());

        var overrideRule = doc.RootElement.GetProperty("ruleOverrides")[0];
        Assert.Equal("global-rule", overrideRule.GetProperty("id").GetString());
        Assert.Equal(0, overrideRule.GetProperty("requiredTags").GetArrayLength());
    }

    [Fact]
    public void PopulateConfigFromNamespace_ParsesStringBooleans_ForOtherConfigNamespaces()
    {
        // Arrange
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                FileTransportType = RepositoryFileTransportType.Ftp
            }
        };

        var agentConfig = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "agent",
            Configuration = /*lang=json,strict*/ """
            {
              "rconSyncEnabled": "false"
            }
            """
        }));

        var moderationConfig = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "moderation",
            Configuration = /*lang=json,strict*/ """
            {
              "protectedNameEnforcementEnabled": "false"
            }
            """
        }));

        // Act
        method.Invoke(sut, [model, agentConfig]);
        method.Invoke(sut, [model, moderationConfig]);

        // Assert
        Assert.False(model.AgentConfigRconSyncEnabled);
        Assert.False(model.ModerationProtectedNameEnforcementEnabled);
    }

    [Fact]
    public void PopulateConfigFromNamespace_ScreenshotsNamespace_MapsAllFields()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel();
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "screenshots",
            Configuration = /*lang=json,strict*/ """
            {
                            "enabled": true,
                            "directoryPath": "/screenshots",
                            "filePattern": "*.png",
                            "pollIntervalSeconds": 45
            }
            """
        }));

        method.Invoke(sut, [model, config]);

        Assert.True(model.ScreenshotConfigEnabled);
        Assert.Equal("/screenshots", model.ScreenshotConfigDirectoryPath);
        Assert.Equal("*.png", model.ScreenshotConfigFilePattern);
        Assert.Equal(45, model.ScreenshotConfigPollIntervalSeconds);
    }

    [Fact]
    public void PopulateConfigFromNamespace_FileTransportNamespace_MapsMapsRootPath()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("PopulateConfigFromNamespace");
        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                FileTransportType = RepositoryFileTransportType.Sftp
            }
        };

        var config = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "sftp",
            Configuration = /*lang=json,strict*/ """
            {
              "hostname": "sftp.example.com",
              "port": 22,
              "username": "demo",
              "password": "secret",
              "hostKeyFingerprint": "aa:bb:cc",
              "mapsRootPath": "/customer-a/server1"
            }
            """
        }));

        method.Invoke(sut, [model, config]);

        Assert.Equal("/customer-a/server1", model.FileTransportConfigMapsRootPath);
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
        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
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

        Assert.False(upsertPayloads.ContainsKey("funnyMessages"));
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_AgentEnabled_BroadcastsDisabled_DeletesBroadcastsNamespace()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("SaveConfigNamespacesAsync");
        var gameServerId = Guid.NewGuid();
        var upsertPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deletedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, UpsertConfigurationDto dto, CancellationToken _) =>
            {
                upsertPayloads[ns] = dto.Configuration;
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.DeleteConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, CancellationToken _) =>
            {
                deletedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = gameServerId,
                Title = "Server Alpha",
                AgentEnabled = true
            },
            BroadcastsEnabled = null,
            BroadcastMessages =
            [
                new BroadcastMessageViewModel { Message = "^1Welcome", Enabled = true }
            ]
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.False(upsertPayloads.ContainsKey("broadcasts"));
        Assert.Contains("broadcasts", deletedNamespaces);
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_CanConfigureScreenshots_UpsertsScreenshotsContract()
    {
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
            ScreenshotConfigEnabled = true,
            ScreenshotConfigDirectoryPath = "/screenshots",
            ScreenshotConfigFilePattern = "*.png",
            ScreenshotConfigPollIntervalSeconds = 45
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, true, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.True(upsertPayloads.TryGetValue("screenshots", out var screenshotsJson));
        using var doc = System.Text.Json.JsonDocument.Parse(screenshotsJson);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal("/screenshots", root.GetProperty("directoryPath").GetString());
        Assert.Equal("*.png", root.GetProperty("filePattern").GetString());
        Assert.Equal(45, root.GetProperty("pollIntervalSeconds").GetInt32());
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_CanEditFileTransport_UpsertsMapsRootPath()
    {
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
                FileTransportType = RepositoryFileTransportType.Sftp
            },
            FileTransportConfigHostname = "sftp.example.com",
            FileTransportConfigPort = 22,
            FileTransportConfigUsername = "demo",
            FileTransportConfigPassword = "secret",
            FileTransportConfigHostKeyFingerprint = "aa:bb:cc",
            FileTransportConfigMapsRootPath = "/customer-a/server1"
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, true, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.True(upsertPayloads.TryGetValue("sftp", out var transportJson));
        using var doc = System.Text.Json.JsonDocument.Parse(transportJson);
        var root = doc.RootElement;
        Assert.Equal("/customer-a/server1", root.GetProperty("mapsRootPath").GetString());
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_WithDisabledFeatures_DoesNotCallDeleteConfigurationAsync()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("SaveConfigNamespacesAsync");
        var gameServerId = Guid.NewGuid();
        var upsertedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, UpsertConfigurationDto _, CancellationToken _) =>
            {
                upsertedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.DeleteConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, CancellationToken _) =>
            {
                deletedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = gameServerId,
                Title = "Server Alpha",
                AgentEnabled = false,
                BanFileSyncEnabled = false,
                ServerListEnabled = false
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.DoesNotContain(AgentSettingsConstants.Namespace, upsertedNamespaces);
        Assert.DoesNotContain(BanFileSettingsConstants.Namespace, upsertedNamespaces);
        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, upsertedNamespaces);
        Assert.Empty(deletedNamespaces);
    }

    [Fact]
    public async Task SaveConfigNamespacesAsync_WithReenable_UpsertsThenIgnoresDelete()
    {
        var sut = CreateSut();
        var method = GetPrivateInstanceMethod("SaveConfigNamespacesAsync");
        var gameServerId = Guid.NewGuid();
        var upsertedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, UpsertConfigurationDto _, CancellationToken _) =>
            {
                upsertedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.DeleteConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string ns, CancellationToken _) =>
            {
                deletedNamespaces.Add(ns);
                return new ApiResult(HttpStatusCode.OK, new ApiResponse());
            });

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = gameServerId,
                Title = "Server Alpha",
                AgentEnabled = true,
                BanFileSyncEnabled = true,
                ServerListEnabled = true
            },
            ChatCommands = new ChatCommandServerSettingsViewModel
            {
                Commands =
                [
                    new ChatCommandServerEntryViewModel
                    {
                        Name = "fu",
                        Prefix = "!fu",
                        Usage = "!fu <player name>",
                        OverrideEnabled = true,
                        Enabled = false
                    }
                ]
            }
        };

        var task = (Task)method.Invoke(sut, [model, gameServerId, false, false, false, new List<string>(), CancellationToken.None])!;
        await task;

        Assert.Contains(AgentSettingsConstants.Namespace, upsertedNamespaces);
        Assert.Contains(ChatCommandSettingsConstants.Namespace, upsertedNamespaces);
        Assert.Contains(BanFileSettingsConstants.Namespace, upsertedNamespaces);
        Assert.Contains(ServerListSettingsConstants.Namespace, upsertedNamespaces);

        Assert.DoesNotContain(AgentSettingsConstants.Namespace, deletedNamespaces);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, deletedNamespaces);
        Assert.DoesNotContain(BanFileSettingsConstants.Namespace, deletedNamespaces);
        Assert.DoesNotContain(ServerListSettingsConstants.Namespace, deletedNamespaces);
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
            Configuration = /*lang=json,strict*/ "{\"hostname\":\"sftp.example.com\",\"port\":22,\"username\":\"test-user\",\"password\":\"test-pass\"}",
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

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = existingServer.Platform,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = true,
                FileTransportType = RepositoryFileTransportType.Sftp,
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
        Assert.Equal(RepositoryFileTransportType.Ftp, capturedUpdate.FileTransportType);
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

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = existingServer.Platform,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = false,
                FileTransportEnabled = true,
                FileTransportType = RepositoryFileTransportType.Sftp,
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
        Assert.Equal(RepositoryFileTransportType.Sftp, capturedUpdate.FileTransportType);
        Assert.Null(capturedUpdate.FtpEnabled);
    }

    [Fact]
    public async Task Edit_WhenDependencyPrerequisitesAreOff_DoesNotAutoUnsetAgentAndBanFileSync()
    {
        // Arrange
        var existingServer = CreateGameServerDto(ftpEnabled: true, fileTransportEnabled: true, fileTransportType: "Ftp");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.GetConfigurations(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([]))));

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
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = existingServer.Platform,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = true,
                FileTransportEnabled = false,
                FileTransportType = RepositoryFileTransportType.Ftp,
                RconEnabled = false,
                BanFileSyncEnabled = true,
                BanFileRootPath = "/",
                ServerListEnabled = false
            }
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Edit(model, CancellationToken.None);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(capturedUpdate);
        Assert.True(capturedUpdate.AgentEnabled);
        Assert.True(capturedUpdate.BanFileSyncEnabled);
        Assert.False(capturedUpdate.FileTransportEnabled);
        Assert.False(capturedUpdate.RconEnabled);
    }

    [Fact]
    public async Task Edit_WhenPlatformIsPosted_UsesPostedPlatform()
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
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var tamperedPlatform = existingServer.Platform == RepositoryGameServerPlatform.Windows
            ? RepositoryGameServerPlatform.Linux
            : RepositoryGameServerPlatform.Windows;

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = tamperedPlatform,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = existingServer.FileTransportEnabled,
                FileTransportType = existingServer.FileTransportType,
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
        Assert.Equal(tamperedPlatform, capturedUpdate.Platform);
    }

    [Fact]
    public async Task Edit_WhenPlatformIsUnknown_ReturnsViewWithModelError()
    {
        // Arrange
        var existingServer = CreateGameServerDto(ftpEnabled: true, fileTransportEnabled: true, fileTransportType: "Ftp");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = RepositoryGameServerPlatform.Unknown,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = existingServer.FileTransportEnabled,
                FileTransportType = existingServer.FileTransportType,
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
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(sut.ModelState.IsValid);
        Assert.True(sut.ModelState.TryGetValue("GameServer.Platform", out var platformModelState));
        Assert.NotNull(platformModelState);
        Assert.Contains(platformModelState.Errors, static e => string.Equals(e.ErrorMessage, "Platform is required.", StringComparison.Ordinal));

        mockRepositoryApiClient.Verify(x => x.GameServers.V1.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_WhenPlatformEnumIsInvalid_ReturnsViewWithModelError()
    {
        // Arrange
        var existingServer = CreateGameServerDto(ftpEnabled: true, fileTransportEnabled: true, fileTransportType: "Ftp");

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = (RepositoryGameServerPlatform)999,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = existingServer.FileTransportEnabled,
                FileTransportType = existingServer.FileTransportType,
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
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(sut.ModelState.IsValid);
        Assert.True(sut.ModelState.TryGetValue("GameServer.Platform", out var platformModelState));
        Assert.NotNull(platformModelState);
        Assert.Contains(platformModelState.Errors, static e => string.Equals(e.ErrorMessage, "Platform is required.", StringComparison.Ordinal));

        mockRepositoryApiClient.Verify(x => x.GameServers.V1.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_WhenCod4xPluginConfigIsMalformed_DoesNotBlockAndSkipsCod4xPluginNamespaceMutation()
    {
        // Arrange
        var existingServer = JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "CoD4x Test Server",
            GameType = RepositoryGameType.CallOfDuty4x,
            Platform = "Windows",
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = "Ftp",
            FtpEnabled = false,
            RconEnabled = false,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1
        }))!;

        var malformedCod4xPluginConfig = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = "cod4xPlugin",
            Configuration = "{ bad-json",
            LastModifiedUtc = DateTime.UtcNow
        }))!;

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(existingServer)));

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.GetConfigurations(existingServer.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([malformedCod4xPluginConfig]))));

        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.UpdateGameServer(It.IsAny<EditGameServerDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.UpsertConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        mockRepositoryApiClient
            .Setup(x => x.GameServerConfigurations.V1.DeleteConfiguration(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Write))
            .ReturnsAsync(AuthorizationResult.Success());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_FileTransport_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Credentials_Rcon_Write))
            .ReturnsAsync(AuthorizationResult.Failed());

        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Admin_Screenshots_Configure))
            .ReturnsAsync(AuthorizationResult.Failed());

        var model = new GameServerEditViewModel
        {
            GameServer = new GameServerViewModel
            {
                GameServerId = existingServer.GameServerId,
                Title = existingServer.Title,
                GameType = existingServer.GameType,
                Platform = existingServer.Platform,
                Hostname = existingServer.Hostname,
                QueryPort = existingServer.QueryPort,
                AgentEnabled = existingServer.AgentEnabled,
                FileTransportEnabled = existingServer.FileTransportEnabled,
                FileTransportType = existingServer.FileTransportType,
                RconEnabled = existingServer.RconEnabled,
                BanFileSyncEnabled = existingServer.BanFileSyncEnabled,
                BanFileRootPath = existingServer.BanFileRootPath,
                ServerListEnabled = existingServer.ServerListEnabled,
                ServerListPosition = existingServer.ServerListPosition
            },
            Cod4xInheritPluginSettings = true
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Edit(model, CancellationToken.None);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        mockRepositoryApiClient.Verify(x => x.GameServerConfigurations.V1.UpsertConfiguration(
            It.IsAny<Guid>(),
            Cod4xPluginSettingsConstants.Namespace,
            It.IsAny<UpsertConfigurationDto>(),
            It.IsAny<CancellationToken>()), Times.Never);

        mockRepositoryApiClient.Verify(x => x.GameServerConfigurations.V1.DeleteConfiguration(
            It.IsAny<Guid>(),
            Cod4xPluginSettingsConstants.Namespace,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WhenPlatformEnumIsInvalid_ReturnsViewWithModelError()
    {
        // Arrange
        var model = new GameServerViewModel
        {
            Title = "Test Server",
            GameType = RepositoryGameType.CallOfDuty4,
            Platform = (RepositoryGameServerPlatform)999,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            BanFileRootPath = "/"
        };

        var sut = CreateSut();

        // Act
        var result = await sut.Create(model, CancellationToken.None);

        // Assert
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Same(model, viewResult.Model);
        Assert.False(sut.ModelState.IsValid);
        Assert.True(sut.ModelState.TryGetValue(nameof(GameServerViewModel.Platform), out var platformModelState));
        Assert.NotNull(platformModelState);
        Assert.Contains(platformModelState.Errors, static e => string.Equals(e.ErrorMessage, "Platform is required.", StringComparison.Ordinal));

        mockRepositoryApiClient.Verify(x => x.GameServers.V1.CreateGameServer(It.IsAny<CreateGameServerDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static GameServerDto CreateGameServerDto(bool ftpEnabled, bool fileTransportEnabled, string fileTransportType)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "Test Server",
            GameType = RepositoryGameType.CallOfDuty4,
            Platform = "Windows",
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
