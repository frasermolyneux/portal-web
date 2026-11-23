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
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class SiegeModeControllerTests
{
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> authorization = new();
    private readonly Guid serverId = Guid.NewGuid();

    [Theory]
    [InlineData("DryRun", SiegeSessionMode.DryRun)]
    [InlineData("Enforce", SiegeSessionMode.Enforce)]
    public async Task Activate_UsesAuthenticatedActorAndConfirmedMode(string mode, SiegeSessionMode expected)
    {
        SetupServer(true);
        var operation = Guid.NewGuid();
        repository.Setup(r => r.SiegeMode.V1.StartSession(serverId,
                It.Is<StartSiegeSessionDto>(s => s.Actor == "forum-id" && s.ExpectedDurationMinutes == 240 &&
                    s.OperationId == operation && s.Mode == expected), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.Created,
                new ApiResponse<SiegeSessionDto>(new SiegeSessionDto { SessionId = operation })));

        var result = await CreateSut().Activate(serverId, operation, 240, mode, default);

        Assert.IsType<RedirectToActionResult>(result);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    [InlineData("enforce")]
    public async Task Activate_RejectsMissingOrInvalidMode(string? mode)
    {
        SetupServer(true);
        var result = await CreateSut().Activate(serverId, Guid.NewGuid(), 240, mode, default);
        Assert.IsType<BadRequestResult>(result);
        repository.Verify(r => r.SiegeMode.V1.StartSession(It.IsAny<Guid>(), It.IsAny<StartSiegeSessionDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateForm_UsesDefaultDurationWhenNamespaceIsAbsent()
    {
        SetupServer(true);
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration("siegeMode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.NotFound));

        var view = Assert.IsType<ViewResult>(await CreateSut().Activate(serverId, default));
        Assert.Equal(240, Assert.IsType<SiegeModeActivationViewModel>(view.Model).DurationMinutes);
    }

    [Fact]
    public async Task ActivateForm_RejectsMalformedSavedSettings()
    {
        SetupServer(true);
        var config = JsonConvert.DeserializeObject<ConfigurationDto>(/*lang=json,strict*/ "{\"Namespace\":\"siegeMode\",\"Configuration\":\"not-json\"}")!;
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration("siegeMode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(config)));

        Assert.IsType<UnprocessableEntityObjectResult>(await CreateSut().Activate(serverId, default));
    }

    [Fact]
    public async Task Activate_DoesNotCallRepositoryWhenGameIsNotAuthorized()
    {
        SetupServer(false);
        var result = await CreateSut().Activate(serverId, Guid.NewGuid(), 240, "Enforce", default);
        Assert.IsType<ForbidResult>(result);
        repository.Verify(r => r.SiegeMode.V1.StartSession(It.IsAny<Guid>(), It.IsAny<StartSiegeSessionDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_RequiresReconfirmationOnDurationConflict()
    {
        SetupServer(true);
        repository.Setup(r => r.SiegeMode.V1.StartSession(serverId, It.IsAny<StartSiegeSessionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.Conflict));
        var result = Assert.IsType<RedirectToActionResult>(await CreateSut().Activate(serverId, Guid.NewGuid(), 240, "DryRun", default));
        Assert.Equal(nameof(SiegeModeController.Activate), result.ActionName);
    }

    [Fact]
    public async Task End_DoesNotMutateWhenGameIsNotAuthorized()
    {
        SetupServer(false);
        var result = await CreateSut().End(serverId, Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<ForbidResult>(result);
        repository.Verify(r => r.SiegeMode.V1.EndSession(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<ChangeSiegeSessionDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Status_ReturnsUnavailableRatherThanInactiveWhenRepositoryFails()
    {
        SetupServer(true);
        repository.Setup(r => r.SiegeMode.V1.GetCurrentSession(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.ServiceUnavailable));
        var result = Assert.IsType<StatusCodeResult>(await CreateSut().Status(serverId, default));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task Extend_SendsCurrentActorAndRequestedMinutes()
    {
        SetupServer(true);
        var sessionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        repository.Setup(r => r.SiegeMode.V1.ExtendSession(serverId, sessionId,
                It.Is<ChangeSiegeSessionDto>(s => s.Actor == "forum-id" && s.OperationId == operationId && s.ExtensionMinutes == 60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.OK, new ApiResponse<SiegeSessionDto>(new SiegeSessionDto())));
        Assert.IsType<RedirectToActionResult>(await CreateSut().Extend(serverId, sessionId, operationId, 60, default));
        repository.VerifyAll();
    }

    private void SetupServer(bool allowed)
    {
        var server = JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = serverId,
            GameType = GameType.CallOfDuty4,
            Title = "Production server"
        }))!;
        repository.Setup(r => r.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4,
                AuthPolicies.GameServers_Admin_SiegeMode))
            .ReturnsAsync(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private SiegeModeController CreateSut()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(UserProfileClaimType.XtremeIdiotsId, "forum-id")], "test"));
        var http = new DefaultHttpContext { User = user };
        var controller = new SiegeModeController(repository.Object, authorization.Object,
            new NamespaceSettingsParser(), new TelemetryClient(new TelemetryConfiguration()),
            Mock.Of<ILogger<SiegeModeController>>(), Mock.Of<IConfiguration>(), Mock.Of<IAuditLogger>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
        return controller;
    }
}
