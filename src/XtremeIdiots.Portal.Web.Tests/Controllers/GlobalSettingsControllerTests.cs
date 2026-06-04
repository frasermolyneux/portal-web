using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Reflection;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
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
                                "requiredTags": ["tag-a"],
                                "requiredClaims": ["claim-a"]
                            },
                            "commands": {
                                "fu": {
                                    "enabled": true,
                                    "freshnessSeconds": 9,
                                    "requiredTags": ["tag-fu"],
                                    "requiredClaims": ["claim-fu"],
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
        Assert.Equal("claim-a", model.ChatCommands.DefaultRequiredClaims);

        var fu = model.ChatCommands.Commands.Single(x => x.Name == "fu");
        Assert.True(fu.Enabled);
        Assert.Equal(9, fu.FreshnessSeconds);
        Assert.Equal("tag-fu", fu.RequiredTags);
        Assert.Equal("claim-fu", fu.RequiredClaims);
        Assert.Single(fu.Messages);
        Assert.Equal("fu-{name}", fu.Messages[0].Message);
        Assert.True(fu.Messages[0].Enabled);
    }
}
