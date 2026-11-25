using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class RulesAuditControllerTests
{
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Guid serverId = Guid.NewGuid();

    private RulesAuditController CreateController()
    {
        var server = JsonConvert.DeserializeObject<GameServerDto>(
            JsonConvert.SerializeObject(new { GameServerId = serverId, GameType = GameType.CallOfDuty4, Title = "Test server" }))!;
        repository.Setup(api => api.GameServers.V1.GetGameServers(
                null, null, null, It.IsAny<int>(), It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))));
        return new RulesAuditController(repository.Object, NullLogger<RulesAuditController>.Instance);
    }

    private void SetExecutions(params RuleExecutionAuditDto[] items)
        => repository.Setup(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>(items))));

    [Fact]
    public async Task Index_SearchesAcrossFeaturesAndServersByDefault()
    {
        var controller = CreateController();
        var items = new[]
        {
            new RuleExecutionAuditDto { GameServerId = serverId, FeatureId = "welcome-messages" },
            new RuleExecutionAuditDto { GameServerId = Guid.NewGuid(), FeatureId = "vpn-protection" }
        };
        SetExecutions(items);

        var view = Assert.IsType<ViewResult>(await controller.Index(null, null, null, null));
        var model = Assert.IsType<RulesAuditViewModel>(view.Model);
        Assert.Equal(items, model.Executions);
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
            It.Is<RuleExecutionAuditQueryDto>(query => query.GameServerId == null && query.FeatureId == null &&
                query.FromUtc == null && query.ToUtc == null && query.MatchStatus == null && query.SkipEntries == 0 && query.TakeEntries == 20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_SendsFilteredUtcBoundsAndPage()
    {
        var controller = CreateController();
        SetExecutions(new RuleExecutionAuditDto
        {
            GameServerId = serverId,
            FeatureId = "welcome-messages",
            MatchStatus = RuleExecutionMatchStatus.Matched,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)
        });

        var view = Assert.IsType<ViewResult>(await controller.Index(serverId, "welcome-messages",
            "2026-01-01T10:00", "2026-01-01T15:00:00+02:00", 2, RuleExecutionMatchStatus.Matched));
        var model = Assert.IsType<RulesAuditViewModel>(view.Model);
        Assert.Single(model.Executions);
        Assert.Equal(RuleExecutionMatchStatus.Matched, model.MatchStatus);
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
            It.Is<RuleExecutionAuditQueryDto>(query => query.GameServerId == serverId &&
                query.FeatureId == "welcome-messages" && query.MatchStatus == RuleExecutionMatchStatus.Matched &&
                query.SkipEntries == 40 && query.TakeEntries == 20 &&
                query.FromUtc == new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) &&
                query.ToUtc == new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc) &&
                query.FromUtc.Value.Kind == DateTimeKind.Utc && query.ToUtc.Value.Kind == DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Index_RejectsInvalidPage(int page)
    {
        Assert.IsType<BadRequestResult>(await CreateController().Index(null, null, null, null, page));
    }

    [Fact]
    public async Task Index_RejectsInvalidFiltersBeforeCallingRepository()
    {
        var controller = CreateController();
        Assert.IsType<BadRequestResult>(await controller.Index(Guid.Empty, null, null, null));
        Assert.IsType<BadRequestResult>(await controller.Index(null, new string('x', 65), null, null));
        Assert.IsType<BadRequestResult>(await controller.Index(null, null, "not a date", null));
        Assert.IsType<BadRequestResult>(await controller.Index(null, null, "2026-01-01T00:00Z", "2026-01-01T00:00Z"));
        Assert.IsType<BadRequestResult>(await controller.Index(null, null, null, null, matchStatus: (RuleExecutionMatchStatus)99));
        controller.ModelState.AddModelError("serverId", "Invalid server");
        Assert.IsType<BadRequestResult>(await controller.Index(null, null, null, null));
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
            It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_OffersNextPageForFullResults()
    {
        var controller = CreateController();
        SetExecutions(Enumerable.Range(0, 20).Select(_ => new RuleExecutionAuditDto { GameServerId = serverId }).ToArray());
        var view = Assert.IsType<ViewResult>(await controller.Index(null, null, null, null));
        Assert.True(Assert.IsType<RulesAuditViewModel>(view.Model).HasNext);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task Index_UsesFilteredCountForNextPage(int count, bool hasNext)
    {
        var controller = CreateController();
        var items = Enumerable.Range(0, 20).Select(_ => new RuleExecutionAuditDto { GameServerId = serverId }).ToArray();
        var data = new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>(items))
        {
            Pagination = new ApiPagination(count, count, 0, 20)
        };
        repository.Setup(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK, data));

        var view = Assert.IsType<ViewResult>(await controller.Index(null, null, null, null));
        Assert.Equal(hasNext, Assert.IsType<RulesAuditViewModel>(view.Model).HasNext);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_RejectsOutOfScopeResults(bool foreignServer)
    {
        var controller = CreateController();
        SetExecutions(new RuleExecutionAuditDto
        {
            GameServerId = foreignServer ? Guid.NewGuid() : serverId,
            FeatureId = foreignServer ? "welcome-messages" : "vpn-protection"
        });
        var result = Assert.IsType<StatusCodeResult>(await controller.Index(serverId, "welcome-messages", null, null));
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task Index_DoesNotPresentUpstreamFailureAsEmptyHistory()
    {
        var controller = CreateController();
        repository.Setup(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.InternalServerError));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Index(null, null, null, null)).StatusCode);
    }

    [Fact]
    public async Task Index_DoesNotTreatUnknownAsNoMatch()
    {
        var controller = CreateController();
        SetExecutions(new RuleExecutionAuditDto { GameServerId = serverId, MatchStatus = RuleExecutionMatchStatus.Unknown });
        var result = Assert.IsType<StatusCodeResult>(await controller.Index(
            serverId, null, null, null, matchStatus: RuleExecutionMatchStatus.NoMatch));
        Assert.Equal(503, result.StatusCode);
    }

    [Theory]
    [InlineData(RuleExecutionMatchStatus.Matched)]
    [InlineData(RuleExecutionMatchStatus.NoMatch)]
    [InlineData(RuleExecutionMatchStatus.Unknown)]
    public async Task Index_CombinesMatchStatusWithFeatureAndPagination(RuleExecutionMatchStatus matchStatus)
    {
        var controller = CreateController();
        SetExecutions(new RuleExecutionAuditDto
        {
            GameServerId = serverId,
            FeatureId = "vpn-protection",
            MatchStatus = matchStatus
        });

        var view = Assert.IsType<ViewResult>(await controller.Index(serverId, "vpn-protection", null, null, 1, matchStatus));
        Assert.Equal(matchStatus, Assert.IsType<RulesAuditViewModel>(view.Model).MatchStatus);
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
            It.Is<RuleExecutionAuditQueryDto>(query => query.GameServerId == serverId &&
                query.FeatureId == "vpn-protection" && query.MatchStatus == matchStatus &&
                query.SkipEntries == 20 && query.TakeEntries == 20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_RejectsResultsOutsideRequestedTimeRange()
    {
        var controller = CreateController();
        SetExecutions(new RuleExecutionAuditDto
        {
            CreatedAtUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        });
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Index(
            null, null, "2026-01-01T00:00Z", "2026-01-02T00:00Z")).StatusCode);
    }

    [Fact]
    public async Task Index_FailsWhenServerListIsUnavailable()
    {
        var controller = CreateController();
        repository.Setup(api => api.GameServers.V1.GetGameServers(
                null, null, null, It.IsAny<int>(), It.IsAny<int>(), GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.InternalServerError));

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Index(null, null, null, null)).StatusCode);
        repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
            It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_LoadsAllServerPagesAndRejectsDuplicateEntries(bool duplicate)
    {
        var controller = CreateController();
        var servers = Enumerable.Range(0, 101).Select(index =>
            JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
            {
                GameServerId = Guid.NewGuid(),
                GameType = GameType.CallOfDuty4,
                Title = $"Server {index}"
            }))!).ToArray();
        repository.Setup(api => api.GameServers.V1.GetGameServers(
                null, null, null, 0, 100, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers.Take(100).ToArray()))));
        repository.Setup(api => api.GameServers.V1.GetGameServers(
                null, null, null, 100, 100, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([servers[duplicate ? 0 : 100]]))));
        SetExecutions();

        var result = await controller.Index(null, null, null, null);
        if (duplicate)
        {
            Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
            repository.Verify(api => api.RuleExecutions.V1.SearchRuleExecutionAudit(
                It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        else
        {
            var model = Assert.IsType<RulesAuditViewModel>(Assert.IsType<ViewResult>(result).Model);
            Assert.Equal(101, model.Servers.Count);
        }
    }

    [Fact]
    public async Task Execution_ReturnsOnlyTheRequestedFlow()
    {
        var controller = CreateController();
        var id = Guid.NewGuid();
        var flow = new RuleExecutionFlowDto { Execution = new RuleExecutionAuditDto { ExecutionId = id } };
        repository.Setup(api => api.RuleExecutions.V1.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));

        Assert.Same(flow, Assert.IsType<ViewResult>(await controller.Execution(id)).Model);
        var partial = Assert.IsType<PartialViewResult>(await controller.Execution(id, fragment: true));
        Assert.Equal("_ExecutionFlow", partial.ViewName);
        Assert.Same(flow, partial.Model);
        Assert.IsType<BadRequestResult>(await controller.Execution(Guid.Empty));
    }

    [Fact]
    public async Task Execution_HandlesMissingFailedAndMismatchedResponses()
    {
        var controller = CreateController();
        var id = Guid.NewGuid();
        repository.Setup(api => api.RuleExecutions.V1.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.NotFound));
        Assert.IsType<NotFoundResult>(await controller.Execution(id));

        repository.Setup(api => api.RuleExecutions.V1.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.InternalServerError));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Execution(id)).StatusCode);

        repository.Setup(api => api.RuleExecutions.V1.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK,
                new ApiResponse<RuleExecutionFlowDto>(new RuleExecutionFlowDto())));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Execution(id)).StatusCode);
    }
}
