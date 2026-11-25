using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class SiegeAuditControllerTests
{
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IAuthorizationService> authorization = new();
    private readonly Mock<ILogger<SiegeAuditController>> logger = new();
    private readonly Guid allowedId = Guid.NewGuid();
    private readonly Guid deniedId = Guid.NewGuid();

    private SiegeAuditController CreateController()
    {
        authorization.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AuthPolicies.GameServers_Admin_SiegeMode))
            .ReturnsAsync((ClaimsPrincipal _, object? resource, string _) =>
                resource is GameType.CallOfDuty4 ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        var allowed = Server(allowedId, GameType.CallOfDuty4);
        var denied = Server(deniedId, GameType.CallOfDuty2);
        repository.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(allowed, denied));

        var controller = new SiegeAuditController(repository.Object, authorization.Object, logger.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
        };
        return controller;
    }

    [Fact]
    public async Task Sessions_FiltersServersBeforeSearchingAndDoesNotExposeForeignRows()
    {
        var controller = CreateController();
        var allowed = new SiegeSessionDto();
        var denied = new SiegeSessionDto();
        Set(allowed, nameof(SiegeSessionDto.GameServerId), allowedId);
        Set(denied, nameof(SiegeSessionDto.GameServerId), deniedId);
        repository.Setup(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(allowed, denied));

        var view = Assert.IsType<ViewResult>(await controller.Sessions(null, null, null));
        var model = Assert.IsType<SiegeAuditViewModel>(view.Model);
        Assert.Equal(allowedId, Assert.Single(model.Query.GameServerIds));
        Assert.Same(allowed, Assert.Single(model.Sessions));
        repository.Verify(r => r.SiegeMode.V1.SearchSessions(
            It.Is<SiegeAuditQueryDto>(q => q.GameServerIds.Length == 1 && q.GameServerIds[0] == allowedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sessions_OnlyLinksVerifiedStartingUsers()
    {
        var controller = CreateController();
        var resolved = new SiegeSessionDto();
        var mismatch = new SiegeSessionDto();
        var nonUser = new SiegeSessionDto();
        Set(resolved, nameof(SiegeSessionDto.GameServerId), allowedId);
        Set(mismatch, nameof(SiegeSessionDto.GameServerId), allowedId);
        Set(nonUser, nameof(SiegeSessionDto.GameServerId), allowedId);
        Set(resolved, nameof(SiegeSessionDto.StartedBy), "123");
        Set(mismatch, nameof(SiegeSessionDto.StartedBy), "456");
        Set(nonUser, nameof(SiegeSessionDto.StartedBy), "system");
        var user = new UserProfileDto();
        Set(user, nameof(UserProfileDto.XtremeIdiotsForumId), "123");
        Set(user, nameof(UserProfileDto.DisplayName), "Admin");
        repository.Setup(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(resolved, mismatch, nonUser));
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(user)));

        var view = Assert.IsType<ViewResult>(await controller.Sessions(null, null, null));
        var model = Assert.IsType<SiegeAuditViewModel>(view.Model);
        Assert.Equal("Admin", Assert.Single(model.StartingUsers).Value);
        Assert.True(model.StartingUsers.ContainsKey("123"));
        repository.Verify(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("system", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Actions_DeniesUnauthorizedServerWithoutAuditCalls()
    {
        var controller = CreateController();
        Assert.IsType<ForbidResult>(await controller.Actions(deniedId, null, null));
        repository.Verify(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SiegeMode.V1.SearchChecks(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Actions_UsesAuthorizedScopeAndSiegeFeatureForActionsAndChecks()
    {
        var controller = CreateController();
        var allowed = new RuleActionExecutionDto { GameServerId = allowedId, FeatureId = RulesEngineCatalogueV1.SiegeModeFeature, ActionTypeId = "message.public" };
        var foreignServer = allowed with { GameServerId = deniedId };
        var foreignFeature = allowed with { FeatureId = RulesEngineCatalogueV1.VpnProtectionFeature };
        repository.Setup(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(allowed, foreignServer, foreignFeature));
        repository.Setup(r => r.SiegeMode.V1.SearchChecks(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection<SiegeCheckDto>());

        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 2, 1);
        var view = Assert.IsType<ViewResult>(await controller.Actions(allowedId, from, to));
        Assert.Same(allowed, Assert.Single(Assert.IsType<SiegeAuditViewModel>(view.Model).Actions));
        repository.Verify(r => r.RuleExecutions.V1.SearchRuleActions(
            It.Is<RuleActionAuditQueryDto>(q => q.GameServerIds.Length == 1 && q.GameServerIds[0] == allowedId &&
                q.FeatureId == RulesEngineCatalogueV1.SiegeModeFeature && q.FromUtc == DateTime.SpecifyKind(from, DateTimeKind.Utc) &&
                q.ToUtc == DateTime.SpecifyKind(to, DateTimeKind.Utc) && q.SkipEntries == 0 && q.TakeEntries == 50),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SiegeMode.V1.SearchChecks(
            It.Is<SiegeAuditQueryDto>(q => q.GameServerIds.Length == 1 && q.GameServerIds[0] == allowedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sessions_EmptyAuthorizedServerListNeverSendsUnscopedQuery()
    {
        var controller = CreateController();
        repository.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, 0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection<GameServerDto>());

        Assert.IsType<ViewResult>(await controller.Sessions(null, null, null));
        repository.Verify(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Actions_WhenActionSearchFailsDoesNotPresentPartialResults()
    {
        var controller = CreateController();
        repository.Setup(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleActionExecutionDto>>(HttpStatusCode.InternalServerError));
        repository.Setup(r => r.SiegeMode.V1.SearchChecks(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection<SiegeCheckDto>());

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Actions(null, null, null)).StatusCode);
        VerifyWarning();
    }

    [Fact]
    public async Task Sessions_RejectsEqualBoundsAfterAuthorizationWithoutSearching()
    {
        var controller = CreateController();
        var time = DateTime.UtcNow;
        Assert.IsType<BadRequestResult>(await controller.Sessions(null, time, time));
        repository.Verify(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sessions_RepeatedServerPageFailsClosedAndLogs()
    {
        var controller = CreateController();
        var page = Enumerable.Range(0, 50).Select(_ => Server(Guid.NewGuid(), GameType.CallOfDuty4)).ToArray();
        repository.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, It.IsAny<int>(), 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(page));

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Sessions(null, null, null)).StatusCode);
        repository.Verify(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyWarning();
    }

    [Fact]
    public async Task Sessions_TooManyAuthorizedServersFailsClosedAndLogs()
    {
        var controller = CreateController();
        var servers = Enumerable.Range(0, 1001).Select(_ => Server(Guid.NewGuid(), GameType.CallOfDuty4)).ToArray();
        repository.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), null, null, It.IsAny<int>(), 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameType[] _, Guid[]? _, GameServerFilter? _, int skip, int take, GameServerOrder? _, CancellationToken _) =>
                OkCollection(servers.Skip(skip).Take(take).ToArray()));

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Sessions(null, null, null)).StatusCode);
        repository.Verify(r => r.SiegeMode.V1.SearchSessions(It.IsAny<SiegeAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyWarning();
    }

    [Fact]
    public async Task Session_UsesRealServerAuthorizationBeforeAnySessionCalls()
    {
        var controller = CreateController();
        var denied = Server(deniedId, GameType.CallOfDuty2);
        repository.Setup(r => r.GameServers.V1.GetGameServer(deniedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(denied)));

        Assert.IsType<ForbidResult>(await controller.Session(deniedId, Guid.NewGuid()));
        repository.Verify(r => r.SiegeMode.V1.GetSessions(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.SiegeMode.V1.GetSessionEvents(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Session_FetchesScopedDetailAndIndependentPages()
    {
        var controller = CreateController();
        var sessionId = Guid.NewGuid();
        var session = new SiegeSessionDto();
        Set(session, nameof(SiegeSessionDto.SessionId), sessionId);
        Set(session, nameof(SiegeSessionDto.GameServerId), allowedId);
        var entry = new SiegeSessionEventDto();
        Set(entry, nameof(SiegeSessionEventDto.SessionId), sessionId);
        var foreign = new SiegeSessionEventDto();
        Set(foreign, nameof(SiegeSessionEventDto.SessionId), Guid.NewGuid());
        repository.Setup(r => r.GameServers.V1.GetGameServer(allowedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(Server(allowedId, GameType.CallOfDuty4))));
        repository.Setup(r => r.SiegeMode.V1.GetSessions(allowedId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(session));
        repository.Setup(r => r.SiegeMode.V1.GetSessionEvents(allowedId, sessionId, 50, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(entry, foreign));
        var action = new RuleActionExecutionDto { GameServerId = allowedId, SiegeSessionId = sessionId, FeatureId = RulesEngineCatalogueV1.SiegeModeFeature, ActionTypeId = "moderation.ban" };
        repository.Setup(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(action, action with { SiegeSessionId = Guid.NewGuid() }, action with { GameServerId = deniedId }));
        repository.Setup(r => r.SiegeMode.V1.GetChecks(allowedId, sessionId, 150, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection<SiegeCheckDto>());

        var view = Assert.IsType<ViewResult>(await controller.Session(allowedId, sessionId, 1, 2, 3));
        var model = Assert.IsType<SiegeSessionDetailViewModel>(view.Model);
        Assert.Same(entry, Assert.Single(model.Events));
        Assert.Same(action, Assert.Single(model.Actions));
        repository.Verify(r => r.RuleExecutions.V1.SearchRuleActions(
            It.Is<RuleActionAuditQueryDto>(q => q.GameServerIds.Length == 1 && q.GameServerIds[0] == allowedId &&
                q.SiegeSessionId == sessionId && q.FeatureId == RulesEngineCatalogueV1.SiegeModeFeature &&
                q.SkipEntries == 100 && q.TakeEntries == 50), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, model.EventPage);
        Assert.Equal(2, model.ActionPage);
        Assert.Equal(3, model.CheckPage);
    }

    [Fact]
    public async Task Session_UnknownSessionDoesNotFetchDetails()
    {
        var controller = CreateController();
        var sessionId = Guid.NewGuid();
        repository.Setup(r => r.GameServers.V1.GetGameServer(allowedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(Server(allowedId, GameType.CallOfDuty4))));
        repository.Setup(r => r.SiegeMode.V1.GetSessions(allowedId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection<SiegeSessionDto>());

        Assert.IsType<NotFoundResult>(await controller.Session(allowedId, sessionId));
        repository.Verify(r => r.SiegeMode.V1.GetSessionEvents(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("actions")]
    [InlineData("checks")]
    public async Task Session_DetailApiFailureReturns503AndLogs(string failure)
    {
        var controller = CreateController();
        var sessionId = Guid.NewGuid();
        var session = new SiegeSessionDto();
        Set(session, nameof(SiegeSessionDto.SessionId), sessionId);
        Set(session, nameof(SiegeSessionDto.GameServerId), allowedId);
        repository.Setup(r => r.GameServers.V1.GetGameServer(allowedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(Server(allowedId, GameType.CallOfDuty4))));
        repository.Setup(r => r.SiegeMode.V1.GetSessions(allowedId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(session));
        repository.Setup(r => r.SiegeMode.V1.GetSessionEvents(allowedId, sessionId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure == "events" ? new ApiResult<CollectionModel<SiegeSessionEventDto>>(HttpStatusCode.ServiceUnavailable) : OkCollection<SiegeSessionEventDto>());
        repository.Setup(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure == "actions" ? new ApiResult<CollectionModel<RuleActionExecutionDto>>(HttpStatusCode.ServiceUnavailable) : OkCollection<RuleActionExecutionDto>());
        repository.Setup(r => r.SiegeMode.V1.GetChecks(allowedId, sessionId, 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure == "checks" ? new ApiResult<CollectionModel<SiegeCheckDto>>(HttpStatusCode.ServiceUnavailable) : OkCollection<SiegeCheckDto>());

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Session(allowedId, sessionId)).StatusCode);
        VerifyWarning();
    }

    [Fact]
    public async Task Session_RejectsOverflowingPageAfterServerAuthorization()
    {
        var controller = CreateController();
        repository.Setup(r => r.GameServers.V1.GetGameServer(allowedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(Server(allowedId, GameType.CallOfDuty4))));
        Assert.IsType<BadRequestResult>(await controller.Session(allowedId, Guid.NewGuid(), int.MaxValue));
        repository.Verify(r => r.SiegeMode.V1.GetSessions(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidInput_DoesNotBypassServerAuthorization()
    {
        var controller = CreateController();
        repository.Setup(r => r.GameServers.V1.GetGameServer(deniedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(Server(deniedId, GameType.CallOfDuty2))));

        Assert.IsType<ForbidResult>(await controller.Session(deniedId, Guid.NewGuid(), int.MaxValue));
        Assert.IsType<ForbidResult>(await controller.Actions(deniedId, null, null, -1));
        repository.Verify(r => r.SiegeMode.V1.GetSessions(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.RuleExecutions.V1.SearchRuleActions(It.IsAny<RuleActionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void VerifyWarning()
        => logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

    private static GameServerDto Server(Guid id, GameType game)
    {
        var result = new GameServerDto();
        Set(result, nameof(GameServerDto.GameServerId), id);
        Set(result, nameof(GameServerDto.GameType), game);
        return result;
    }

    private static void Set<T>(object target, string property, T value) => target.GetType().GetProperty(property)!.SetValue(target, value);

    private static ApiResult<CollectionModel<T>> OkCollection<T>(params T[] items)
        => new(HttpStatusCode.OK, new ApiResponse<CollectionModel<T>>(new CollectionModel<T>(items)));
}
