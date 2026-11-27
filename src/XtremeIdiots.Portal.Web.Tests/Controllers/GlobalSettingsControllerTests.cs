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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ServerList;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class GlobalSettingsControllerTests
{
    private const string LegacyWelcomeNamespace = "welcomeMessages";

    private static void PrepareSiegeSubmission(GlobalSettingsController controller, GlobalSettingsViewModel model)
    {
        var fields = new Dictionary<string, string>
        {
            ["SiegeMode.DurationMinutes"] = model.SiegeMode.DurationMinutes.ToString(),
            ["SiegeMode.Enabled"] = model.SiegeMode.Enabled.ToString(),
            ["SiegeMode.RulesJson"] = model.SiegeMode.RulesJson,
            ["VpnProtection.Enabled"] = model.VpnProtection.Enabled.ToString(),
            ["VpnProtection.RulesJson"] = model.VpnProtection.RulesJson,
            ["WelcomeRules.Enabled"] = model.WelcomeRules.Enabled.ToString(),
            ["WelcomeRules.ConnectionDelaySeconds"] = model.WelcomeRules.ConnectionDelaySeconds.ToString(),
            ["WelcomeRules.FreshnessSeconds"] = model.WelcomeRules.FreshnessSeconds.ToString(),
            ["WelcomeRules.RulesJson"] = model.WelcomeRules.RulesJson,
            ["ChatCommands.DefaultFreshnessSeconds"] = model.ChatCommands.DefaultFreshnessSeconds.ToString()
        };
        foreach (var (key, value) in fields)
        {
            controller.ModelState.SetModelValue(key, value, value);
            controller.ModelState.MarkFieldValid(key);
        }
    }

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
        SetupConfigurations();
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
    public async Task Index_Get_ReturnsUnavailableWhenConfigurationsCannotBeRetrieved()
    {
        var sut = CreateSut();
        mockRepositoryApiClient
            .Setup(client => client.GlobalConfigurations.V1.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.ServiceUnavailable));

        var result = Assert.IsType<StatusCodeResult>(await sut.Index(default));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task Index_Get_ReturnsUnprocessableEntityWhenSiegeSettingsAreUnreadable()
    {
        var sut = CreateSut();
        SetupConfigurations(JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = SiegeModeSettingsConstants.Namespace,
            Configuration = "not-json"
        }))!);

        var result = Assert.IsType<UnprocessableEntityObjectResult>(await sut.Index(default));

        Assert.Contains("Saved Siege Mode settings are unreadable", Assert.IsType<string>(result.Value));
    }

    [Fact]
    public async Task Index_Post_DoesNotSaveWhenExistingSiegeSettingsAreUnreadable()
    {
        var sut = CreateSut();
        SetupConfigurations(JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = SiegeModeSettingsConstants.Namespace,
            Configuration = "not-json"
        }))!);
        var model = new GlobalSettingsViewModel();
        PrepareSiegeSubmission(sut, model);

        var result = Assert.IsType<UnprocessableEntityObjectResult>(await sut.Index(model));

        Assert.Contains("Saved Siege Mode settings are unreadable", Assert.IsType<string>(result.Value));
        mockRepositoryApiClient.Verify(client => client.GlobalConfigurations.V1.UpsertConfiguration(
            It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()), Times.Never);
        mockRepositoryApiClient.Verify(client => client.GlobalConfigurations.V1.DeleteConfiguration(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_Post_DoesNotSaveWhenExistingVpnSettingsAreInvalid()
    {
        var sut = CreateSut();
        SetupConfigurations(JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = VpnProtectionSettingsConstants.Namespace,
            Configuration = /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"siege-mode","enabled":false,"rules":[]}"""
        }))!);
        var model = new GlobalSettingsViewModel();
        PrepareSiegeSubmission(sut, model);

        var result = Assert.IsType<UnprocessableEntityObjectResult>(await sut.Index(model));

        Assert.Contains("Saved VPN Protection settings are unreadable", Assert.IsType<string>(result.Value));
        mockRepositoryApiClient.Verify(client => client.GlobalConfigurations.V1.UpsertConfiguration(
            It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_Post_DoesNotSaveWhenConfigurationsCannotBeRetrieved()
    {
        var sut = CreateSut();
        mockRepositoryApiClient
            .Setup(client => client.GlobalConfigurations.V1.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(HttpStatusCode.ServiceUnavailable));
        var model = new GlobalSettingsViewModel();
        PrepareSiegeSubmission(sut, model);

        var result = Assert.IsType<StatusCodeResult>(await sut.Index(model));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        mockRepositoryApiClient.Verify(client => client.GlobalConfigurations.V1.UpsertConfiguration(
            It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()), Times.Never);
        mockRepositoryApiClient.Verify(client => client.GlobalConfigurations.V1.DeleteConfiguration(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupConfigurations(params ConfigurationDto[] configurations)
    {
        mockRepositoryApiClient
            .Setup(client => client.GlobalConfigurations.V1.GetConfigurations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(
                    new CollectionModel<ConfigurationDto>(configurations))));
    }

    [Fact]
    public async Task Index_Post_PreservesNewBroadcastDocumentAndSavesOtherSettings()
    {
        var sut = CreateSut();
        SetupConfigurations(JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = BroadcastSettingsConstants.Namespace,
            Configuration = /*lang=json,strict*/ """{"schemaVersion":1,"messagesMode":"Inherit","messages":[]}"""
        }))!);
        var upserted = new List<string>();
        mockRepositoryApiClient
            .Setup(x => x.GlobalConfigurations.V1.UpsertConfiguration(It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ns, UpsertConfigurationDto _, CancellationToken _) =>
            {
                upserted.Add(ns);
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(JsonConvert.DeserializeObject<ConfigurationDto>("{}")));
            });
        var model = new GlobalSettingsViewModel { BroadcastsReadOnly = false, BroadcastsEnabled = true };
        PrepareSiegeSubmission(sut, model);
        sut.ModelState.SetModelValue(nameof(model.BroadcastsIntervalSeconds), "500", "500");
        sut.ModelState.MarkFieldValid(nameof(model.BroadcastsIntervalSeconds));

        Assert.IsType<RedirectToActionResult>(await sut.Index(model));
        Assert.True(model.BroadcastsReadOnly);
        Assert.NotEmpty(upserted);
        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, upserted);
        mockRepositoryApiClient.Verify(x => x.GlobalConfigurations.V1.DeleteConfiguration(
            BroadcastSettingsConstants.Namespace, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_Post_PreservesUneditableOrOmittedChatCommandsAndSavesOtherSettings(bool editableAtPost)
    {
        var sut = CreateSut();
        var configuration = JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = ChatCommandSettingsConstants.Namespace,
            Configuration = editableAtPost
                ? /*lang=json,strict*/ """{"schemaVersion":1,"commands":{"fu":{"enabled":true}}}"""
                : /*lang=json,strict*/ """{"schemaVersion":1,"commands":{"fu":{"enabled":true,"policy":{"rules":[],"defaultDecision":"Deny"},"replies":{"denied":["No"]}}}}"""
        }))!;
        SetupConfigurations(configuration);
        var upserted = new List<string>();
        mockRepositoryApiClient
            .Setup(x => x.GlobalConfigurations.V1.UpsertConfiguration(It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ns, UpsertConfigurationDto _, CancellationToken _) =>
            {
                upserted.Add(ns);
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(new ConfigurationDto()));
            });
        var model = new GlobalSettingsViewModel { ChatCommandsReadOnly = false };
        model.ChatCommands.Commands.Single(command => command.Name == "fu").Enabled = false;
        PrepareSiegeSubmission(sut, model);
        if (editableAtPost)
            sut.ModelState.Remove("ChatCommands.DefaultFreshnessSeconds");
        sut.ModelState.AddModelError("ChatCommands.Commands[3].FreshnessSeconds", "Invalid value");
        sut.ModelState.AddModelError("Commands[3].Messages[0].Message", "Invalid value");
        sut.ModelState.AddModelError("DefaultRequiredTags", "Invalid value");

        Assert.IsType<RedirectToActionResult>(await sut.Index(model));
        Assert.True(model.ChatCommandsReadOnly);
        Assert.True(model.ChatCommands.Commands.Single(command => command.Name == "fu").Enabled);
        Assert.True(sut.ModelState.IsValid);
        Assert.NotEmpty(upserted);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, upserted);
        mockRepositoryApiClient.Verify(x => x.GlobalConfigurations.V1.DeleteConfiguration(
            ChatCommandSettingsConstants.Namespace, It.IsAny<CancellationToken>()), Times.Never);
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

        PrepareSiegeSubmission(sut, model);
        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(upsertPayloads.TryGetValue("chatCommands", out var chatCommandsJson));

        using var doc = System.Text.Json.JsonDocument.Parse(chatCommandsJson);
        Assert.Equal(ChatCommandSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var defaults = doc.RootElement.GetProperty("defaults");

        Assert.False(defaults.TryGetProperty("requiredTags", out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_Post_UsesOnlyAuthoritativeRuleEnvelopeExtensionData(bool hasSavedConfigurations)
    {
        var sut = CreateSut();
        if (hasSavedConfigurations)
        {
            SetupConfigurations(
                JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
                {
                    Namespace = VpnProtectionSettingsConstants.Namespace,
                    Configuration = /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"vpn-protection","enabled":false,"rules":[],"futureData":{"value":"saved-vpn"}}"""
                }))!,
                JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
                {
                    Namespace = SiegeModeSettingsConstants.Namespace,
                    Configuration = /*lang=json,strict*/ """{"schemaVersion":1,"featureId":"siege-mode","enabled":false,"durationMinutes":240,"rules":[],"futureData":{"value":"saved-siege"}}"""
                }))!);
        }

        var upserted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        mockRepositoryApiClient
            .Setup(x => x.GlobalConfigurations.V1.UpsertConfiguration(
                It.IsAny<string>(), It.IsAny<UpsertConfigurationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string ns, UpsertConfigurationDto dto, CancellationToken _) =>
            {
                upserted[ns] = dto.Configuration;
                return new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(new ConfigurationDto()));
            });

        var model = new GlobalSettingsViewModel();
        model.VpnProtection.Enabled = true;
        model.SiegeMode.DurationMinutes = 90;
        model.VpnProtection.EnvelopeExtensionData["futureData"] = System.Text.Json.JsonSerializer.SerializeToElement(new { value = "client-vpn" });
        model.SiegeMode.EnvelopeExtensionData["futureData"] = System.Text.Json.JsonSerializer.SerializeToElement(new { value = "client-siege" });
        model.VpnProtection.EnvelopeExtensionData["injected"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        model.SiegeMode.EnvelopeExtensionData["injected"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        PrepareSiegeSubmission(sut, model);

        Assert.IsType<RedirectToActionResult>(await sut.Index(model));

        using var vpn = System.Text.Json.JsonDocument.Parse(upserted[VpnProtectionSettingsConstants.Namespace]);
        using var siege = System.Text.Json.JsonDocument.Parse(upserted[SiegeModeSettingsConstants.Namespace]);
        Assert.True(vpn.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(90, siege.RootElement.GetProperty("durationMinutes").GetInt32());
        Assert.False(vpn.RootElement.TryGetProperty("injected", out _));
        Assert.False(siege.RootElement.TryGetProperty("injected", out _));
        if (hasSavedConfigurations)
        {
            Assert.Equal("saved-vpn", vpn.RootElement.GetProperty("futureData").GetProperty("value").GetString());
            Assert.Equal("saved-siege", siege.RootElement.GetProperty("futureData").GetProperty("value").GetString());
        }
        else
        {
            Assert.False(vpn.RootElement.TryGetProperty("futureData", out _));
            Assert.False(siege.RootElement.TryGetProperty("futureData", out _));
        }
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

        PrepareSiegeSubmission(sut, model);
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
    public async Task Index_Post_WelcomeRules_UpsertsNewContractWithoutWritingLegacyNamespace()
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
            WelcomeRules = new WelcomeRulesGlobalSettingsViewModel
            {
                Enabled = true,
                ConnectionDelaySeconds = 5,
                FreshnessSeconds = 180,
                RulesJson = /*lang=json,strict*/ """[{"id":"global-rule","enabled":true,"order":0,"condition":{"conditionId":"always","parameters":{}},"actions":[{"id":"a","typeId":"message.public","parameters":{"message":"Welcome {context.player.Name}"},"dependsOnActionIds":[],"templateParameters":["message"]}],"afterMatch":"StopProcessingRules"}]"""
            }
        };

        PrepareSiegeSubmission(sut, model);
        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(upsertPayloads.ContainsKey(LegacyWelcomeNamespace));
        Assert.True(upsertPayloads.TryGetValue(WelcomeRulesSettingsConstants.Namespace, out var welcomeMessagesJson));

        using var doc = System.Text.Json.JsonDocument.Parse(welcomeMessagesJson);
        Assert.Equal(WelcomeRulesSettingsConstants.SchemaVersion, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(5, doc.RootElement.GetProperty("timing").GetProperty("connectionDelaySeconds").GetInt32());
        var firstRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("global-rule", firstRule.GetProperty("id").GetString());
        Assert.Equal("message.public", firstRule.GetProperty("actions")[0].GetProperty("typeId").GetString());
        Assert.Equal("message", firstRule.GetProperty("actions")[0].GetProperty("templateParameters")[0].GetString());
    }

    [Fact]
    public async Task Index_Post_WelcomeRulesDisabled_PreservesRulesAndLegacyNamespace()
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
            WelcomeRules = new WelcomeRulesGlobalSettingsViewModel
            {
                Enabled = false,
                RulesJson = /*lang=json,strict*/ """[{"id":"global-rule","enabled":true,"order":0,"condition":{"conditionId":"always","parameters":{}},"actions":[{"id":"a","typeId":"message.public","parameters":{"message":"Welcome"},"dependsOnActionIds":[]}],"afterMatch":"StopProcessingRules"}]"""
            }
        };

        PrepareSiegeSubmission(sut, model);
        var result = await sut.Index(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(upsertPayloads.ContainsKey(LegacyWelcomeNamespace));
        Assert.True(upsertPayloads.TryGetValue(WelcomeRulesSettingsConstants.Namespace, out var welcomeMessagesJson));

        using var doc = System.Text.Json.JsonDocument.Parse(welcomeMessagesJson);
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
        var firstRule = doc.RootElement.GetProperty("rules")[0];
        Assert.Equal("global-rule", firstRule.GetProperty("id").GetString());
        Assert.Equal("Welcome", firstRule.GetProperty("actions")[0].GetProperty("parameters").GetProperty("message").GetString());

        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.DeleteConfiguration(
                LegacyWelcomeNamespace,
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

        PrepareSiegeSubmission(sut, model);
        sut.ModelState.SetModelValue(nameof(model.BroadcastsIntervalSeconds), "900", "900");
        sut.ModelState.MarkFieldValid(nameof(model.BroadcastsIntervalSeconds));
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

        PrepareSiegeSubmission(sut, model);
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

        PrepareSiegeSubmission(sut, model);
        var result = await sut.Index(model);

        Assert.IsType<ViewResult>(result);
        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.UpsertConfiguration(
                It.IsAny<string>(),
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Index_Post_InvalidVpnRulesJson_DoesNotCallUpsert()
    {
        var model = new GlobalSettingsViewModel
        {
            VpnProtection = new VpnProtectionGlobalSettingsViewModel
            {
                Enabled = true,
                RulesJson = "{"
            }
        };
        var sut = CreateSut();

        PrepareSiegeSubmission(sut, model);
        sut.ModelState.AddModelError(
            $"{nameof(GlobalSettingsViewModel.VpnProtection)}.{nameof(VpnProtectionGlobalSettingsViewModel.RulesJson)}",
            "Invalid rules JSON.");
        var result = await sut.Index(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(sut.ModelState.IsValid);
        Assert.Contains(sut.ModelState, entry =>
            entry.Key == $"{nameof(GlobalSettingsViewModel.VpnProtection)}.{nameof(VpnProtectionGlobalSettingsViewModel.RulesJson)}");
        mockRepositoryApiClient.Verify(
            client => client.GlobalConfigurations.V1.UpsertConfiguration(
                VpnProtectionSettingsConstants.Namespace,
                It.IsAny<UpsertConfigurationDto>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
