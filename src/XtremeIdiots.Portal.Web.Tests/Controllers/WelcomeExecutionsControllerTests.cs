using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class WelcomeExecutionsControllerTests
{
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> authorization = new();
    private readonly Guid serverId = Guid.NewGuid();

    private WelcomeExecutionsController CreateController(bool allowed = true)
    {
        var server = JsonConvert.DeserializeObject<GameServerDto>(
            JsonConvert.SerializeObject(new { GameServerId = serverId, GameType = GameType.CallOfDuty4, Title = "Test server" }))!;
        repository.Setup(api => api.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        authorization.Setup(api => api.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), GameType.CallOfDuty4, AuthPolicies.GameServers_Write))
            .ReturnsAsync(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return new WelcomeExecutionsController(repository.Object, authorization.Object, NullLogger<WelcomeExecutionsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
            }
        };
    }

    private void SetExecutions(params RuleFeatureExecutionDto[] items)
        => repository.Setup(api => api.RuleExecutions.V1.SearchRuleExecutions(It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleFeatureExecutionDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleFeatureExecutionDto>>(new CollectionModel<RuleFeatureExecutionDto>(items))));

    [Fact]
    public async Task Index_RequiresServerWritePermissionBeforeQuerying()
    {
        Assert.IsType<ForbidResult>(await CreateController(false).Index(serverId, null, null));
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutions(
            It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Index_RejectsInvalidPages(int page)
    {
        Assert.IsType<BadRequestResult>(await CreateController().Index(serverId, null, null, page));
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutions(
            It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_RejectsInvalidAndUnderflowingTimeRanges()
    {
        var controller = CreateController();
        var time = DateTime.UtcNow;
        Assert.IsType<BadRequestResult>(await controller.Index(serverId, time, time));
        Assert.IsType<BadRequestResult>(await controller.Index(serverId, null, DateTime.MinValue));
        controller.ModelState.AddModelError("fromUtc", "Invalid date");
        Assert.IsType<BadRequestResult>(await controller.Index(serverId, null, null));
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutions(
            It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_UsesBoundedWelcomeQueryAndPreservesNoActionDiagnostics()
    {
        var controller = CreateController();
        var execution = new RuleFeatureExecutionDto
        {
            GameServerId = serverId,
            FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature,
            State = RuleFeatureExecutionState.Completed,
            Diagnostics = [new RuleExecutionDiagnostic { Code = RuleExecutionDiagnosticCode.NoMatch }]
        };
        SetExecutions(execution);
        var from = new DateTime(2026, 1, 1);
        var to = from.AddDays(1);

        var view = Assert.IsType<ViewResult>(await controller.Index(serverId, from, to, 2));
        var model = Assert.IsType<WelcomeExecutionsViewModel>(view.Model);
        Assert.Same(execution, Assert.Single(model.Executions));
        Assert.False(model.HasNext);
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutions(
            It.Is<RuleFeatureExecutionQueryDto>(query => query.GameServerId == serverId &&
                query.FeatureId == RulesEngineCatalogueV1.WelcomeMessagesFeature && query.SkipEntries == 40 &&
                query.TakeEntries == 20 && query.FromUtc == from && query.ToUtc == to &&
                query.FromUtc.Value.Kind == DateTimeKind.Utc && query.ToUtc.Value.Kind == DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_UsesOneDayDefaultAndOffersNextPageForFullResults()
    {
        var controller = CreateController();
        SetExecutions(Enumerable.Range(0, 20).Select(_ => new RuleFeatureExecutionDto
        {
            GameServerId = serverId,
            FeatureId = RulesEngineCatalogueV1.WelcomeMessagesFeature
        }).ToArray());
        var view = Assert.IsType<ViewResult>(await controller.Index(serverId, null, null));
        var model = Assert.IsType<WelcomeExecutionsViewModel>(view.Model);
        Assert.Equal(TimeSpan.FromDays(1), model.ToUtc - model.FromUtc);
        Assert.True(model.HasNext);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_DoesNotExposeForeignServerOrFeature(bool foreignServer)
    {
        var controller = CreateController();
        SetExecutions(new RuleFeatureExecutionDto
        {
            GameServerId = foreignServer ? Guid.NewGuid() : serverId,
            FeatureId = foreignServer ? RulesEngineCatalogueV1.WelcomeMessagesFeature : RulesEngineCatalogueV1.SiegeModeFeature
        });
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Index(serverId, null, null)).StatusCode);
    }

    [Fact]
    public async Task Index_DoesNotPresentUpstreamFailureAsEmptyHistory()
    {
        var controller = CreateController();
        repository.Setup(api => api.RuleExecutions.V1.SearchRuleExecutions(It.IsAny<RuleFeatureExecutionQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleFeatureExecutionDto>>(HttpStatusCode.InternalServerError));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Index(serverId, null, null)).StatusCode);
    }
}
