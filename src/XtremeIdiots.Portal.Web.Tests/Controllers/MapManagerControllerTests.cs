using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class MapManagerControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PushMap_UsesAuthorizedServersGameType_AndOnlyReportsQueuedWhenAccepted(bool accepted)
    {
        var serverId = Guid.NewGuid();
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            $$"""{"GameServerId":"{{serverId}}","GameType":"CallOfDuty4x","Title":"Synthetic server"}""");
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        var auth = new Mock<IAuthorizationService>();
        auth.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4x, AuthPolicies.GameServers_Maps_Read))
            .ReturnsAsync(AuthorizationResult.Success());
        auth.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.GameServers_Maps_Deploy))
            .ReturnsAsync(AuthorizationResult.Success());
        var sync = new Mock<ISyncApiClient>();
        sync.Setup(x => x.TriggerPushMap(serverId, GameType.CallOfDuty4x, "mp_synthetic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncTriggerResult(accepted, Error: accepted ? null : "synthetic push failure"));
        using var coordinator = new DependencyReadCoordinator();
        using var telemetryConfiguration = new TelemetryConfiguration();
        var sut = new MapManagerController(auth.Object, repository.Object, Mock.Of<IServersApiClient>(), sync.Object,
            Mock.Of<IMapMetadataService>(), coordinator, new TelemetryClient(telemetryConfiguration),
            NullLogger<MapManagerController>.Instance, new ConfigurationBuilder().Build(), Mock.Of<IAuditLogger>());
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "Synthetic")) };
        sut.ControllerContext = new ControllerContext { HttpContext = context };
        sut.TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>());

        var result = await sut.PushMapToRemote(new PushMapToRemoteViewModel { GameServerId = serverId, MapName = "mp_synthetic" });

        Assert.IsType<RedirectToActionResult>(result);
        sync.Verify(x => x.TriggerPushMap(serverId, GameType.CallOfDuty4x, "mp_synthetic", It.IsAny<CancellationToken>()), Times.Once);
        var alerts = sut.TempData["Alerts"]?.ToString();
        Assert.Contains(accepted ? "was queued" : "synthetic push failure", alerts, StringComparison.Ordinal);
        if (!accepted)
            Assert.DoesNotContain("was queued", alerts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public async Task Mutations_RequireBothReadAndDeployAccessBeforeServerChanges(bool canRead, bool canDeploy, bool delete)
    {
        var serverId = Guid.NewGuid();
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            $$"""{"GameServerId":"{{serverId}}","GameType":"CallOfDuty5","Title":"Synthetic server"}""");
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        var auth = new Mock<IAuthorizationService>();
        auth.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty5, AuthPolicies.GameServers_Maps_Read))
            .ReturnsAsync(canRead ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        auth.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty5, AuthPolicies.GameServers_Maps_Deploy))
            .ReturnsAsync(canDeploy ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        var sync = new Mock<ISyncApiClient>();
        var servers = new Mock<IServersApiClient> { DefaultValue = DefaultValue.Mock };
        using var coordinator = new DependencyReadCoordinator();
        using var telemetryConfiguration = new TelemetryConfiguration();
        var sut = new MapManagerController(auth.Object, repository.Object, servers.Object, sync.Object,
            Mock.Of<IMapMetadataService>(), coordinator, new TelemetryClient(telemetryConfiguration),
            NullLogger<MapManagerController>.Instance, new ConfigurationBuilder().Build(), Mock.Of<IAuditLogger>());
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "Synthetic")) };
        sut.ControllerContext = new ControllerContext { HttpContext = context };
        sut.TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>());

        var result = delete
            ? await sut.DeleteMapFromHost(new DeleteMapFromHostModel(serverId, "mp_synthetic"))
            : await sut.PushMapToRemote(new PushMapToRemoteViewModel { GameServerId = serverId, MapName = "mp_synthetic" });

        Assert.IsType<UnauthorizedResult>(result);
        auth.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty5, AuthPolicies.GameServers_Maps_Read), Times.Once);
        auth.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty5, AuthPolicies.GameServers_Maps_Deploy),
            canRead ? Times.Once() : Times.Never());
        sync.Verify(x => x.TriggerPushMap(It.IsAny<Guid>(), It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        servers.Verify(x => x.Maps.V1.DeleteServerMapFromHost(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }
}
