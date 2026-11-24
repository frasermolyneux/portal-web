using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Dashboard;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RecentPlayers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed class ForumIntegrationHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(TestPrincipalProfiles.GameAdmin, HttpStatusCode.Forbidden)]
    public async Task SiegeMode_RequiresSiegeAccess(string? profile, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/siege-mode");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SiegeActivation_RequiresAuthentication()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var response = await host.Client.PostAsJsonAsync(
            $"/api/integration/v1/siege-mode/servers/{Guid.NewGuid()}/activate",
            new { mode = "DryRun", expectedDurationMinutes = 240 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SiegeMode_LoadsAllPagesAndReturnsOnlyAuthorizedServers()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var authorized = Fixture<GameServerDto>(new { GameServerId = Guid.NewGuid(), Title = "^1Production", GameType = GameType.CallOfDuty4 });
        var excluded = Enumerable.Range(0, 200).Select(_ => Fixture<GameServerDto>(new
        {
            GameServerId = Guid.NewGuid(),
            Title = "Other",
            GameType = GameType.CallOfDuty5
        })).ToArray();
        var config = Fixture<ConfigurationDto>(new
        {
            Namespace = SiegeModeSettingsConstants.Namespace,
            Configuration = JsonSerializer.Serialize(new SiegeModeSettingsDocument())
        });
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration(SiegeModeSettingsConstants.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(config)));
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, null, 0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(excluded)) { Pagination = new ApiPagination(300, 201, 0, 200) }));
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, null, 200, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([authorized])) { Pagination = new ApiPagination(300, 201, 200, 200) }));
        repository.Setup(r => r.SiegeMode.V1.GetCurrentSession(authorized.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.NotFound));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/siege-mode");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Portal Test User", body.GetProperty("portalUser").GetProperty("displayName").GetString());
        Assert.Equal(240, body.GetProperty("defaultDurationMinutes").GetInt32());
        var item = Assert.Single(body.GetProperty("servers").EnumerateArray());
        Assert.Equal(authorized.GameServerId, item.GetProperty("gameServerId").GetGuid());
        Assert.Equal("Production", item.GetProperty("name").GetString());
        Assert.Equal("CallOfDuty4", item.GetProperty("gameType").GetString());
        Assert.True(item.GetProperty("canActivate").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("currentSession").ValueKind);
        Assert.Contains(authorized.GameServerId.ToString(), item.GetProperty("portalUrl").GetString());
        repository.Verify(r => r.SiegeMode.V1.GetCurrentSession(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("DryRun", SiegeSessionMode.DryRun)]
    [InlineData("Enforce", SiegeSessionMode.Enforce)]
    public async Task SiegeActivation_UsesActualServerAndSessionActor(string mode, SiegeSessionMode expectedMode)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var serverId = Guid.NewGuid();
        var server = Fixture<GameServerDto>(new { GameServerId = serverId, Title = "Production", GameType = GameType.CallOfDuty4 });
        repository.Setup(r => r.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        repository.Setup(r => r.SiegeMode.V1.StartSession(serverId, It.IsAny<StartSiegeSessionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(HttpStatusCode.OK, new ApiResponse<SiegeSessionDto>(new SiegeSessionDto { SessionId = Guid.NewGuid(), Mode = expectedMode })));
        var path = $"/api/integration/v1/siege-mode/servers/{serverId}/activate";
        using var noToken = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { mode, expectedDurationMinutes = 240 }) };
        noToken.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var rejected = await host.Client.SendAsync(noToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/session");
        sessionRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var session = await host.Client.SendAsync(sessionRequest);
        var tokens = await session.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = Assert.Single(session.Headers.GetValues("Set-Cookie")).Split(';')[0];

        async Task<HttpResponseMessage> Send(string profile, object payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add(tokens.GetProperty("headerName").GetString()!, tokens.GetProperty("requestVerificationToken").GetString()!);
            return await host.Client.SendAsync(request);
        }

        using var denied = await Send(TestPrincipalProfiles.HeadAdminCod5, new { mode, expectedDurationMinutes = 240, gameType = "CallOfDuty5" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedInvalid = await Send(TestPrincipalProfiles.HeadAdminCod5, new { mode = "Unknown", expectedDurationMinutes = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, deniedInvalid.StatusCode);
        using var invalid = await Send(TestPrincipalProfiles.HeadAdmin, new { expectedDurationMinutes = 240 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unknown = await Send(TestPrincipalProfiles.HeadAdmin, new { mode = "Unknown", expectedDurationMinutes = 240 });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var response = await Send(TestPrincipalProfiles.HeadAdmin, new { mode, expectedDurationMinutes = 240, actor = "attacker" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(mode, body.GetProperty("mode").GetString());
        repository.Verify(r => r.SiegeMode.V1.StartSession(serverId, It.Is<StartSiegeSessionDto>(dto =>
            dto.OperationId != Guid.Empty && dto.Actor == "12345" && dto.ExpectedDurationMinutes == 240 && dto.Mode == expectedMode),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task SiegeActivation_PropagatesConflictAndInvalidSettings(HttpStatusCode upstream, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var serverId = Guid.NewGuid();
        repository.Setup(r => r.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(
                Fixture<GameServerDto>(new { GameServerId = serverId, GameType = GameType.CallOfDuty4 }))));
        repository.Setup(r => r.SiegeMode.V1.StartSession(serverId, It.IsAny<StartSiegeSessionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<SiegeSessionDto>(upstream));
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/session");
        sessionRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var session = await host.Client.SendAsync(sessionRequest);
        var tokens = await session.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/integration/v1/siege-mode/servers/{serverId}/activate")
        {
            Content = JsonContent.Create(new { mode = "DryRun", expectedDurationMinutes = 240 })
        };
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        request.Headers.Add("Cookie", Assert.Single(session.Headers.GetValues("Set-Cookie")).Split(';')[0]);
        request.Headers.Add(tokens.GetProperty("headerName").GetString()!, tokens.GetProperty("requestVerificationToken").GetString()!);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SiegeMode_ReportsActiveSessionOrUpstreamFailure(bool failure)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var config = Fixture<ConfigurationDto>(new { Namespace = SiegeModeSettingsConstants.Namespace, Configuration = JsonSerializer.Serialize(new SiegeModeSettingsDocument()) });
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration(SiegeModeSettingsConstants.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(config)));
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, null, 0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(
                    [Fixture<GameServerDto>(new { GameServerId = id, Title = "Production", GameType = GameType.CallOfDuty4 })]))
                { Pagination = new ApiPagination(1, 1, 0, 200) }));
        repository.Setup(r => r.SiegeMode.V1.GetCurrentSession(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure ? new ApiResult<SiegeSessionDto>(HttpStatusCode.ServiceUnavailable) :
                new ApiResult<SiegeSessionDto>(HttpStatusCode.OK, new ApiResponse<SiegeSessionDto>(
                    new SiegeSessionDto { SessionId = sessionId, Mode = SiegeSessionMode.Enforce })));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/siege-mode");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(failure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, response.StatusCode);
        if (!failure)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var item = body.GetProperty("servers")[0];
            Assert.False(item.GetProperty("canActivate").GetBoolean());
            Assert.Equal(sessionId, item.GetProperty("currentSession").GetProperty("sessionId").GetGuid());
            Assert.Equal("Enforce", item.GetProperty("currentSession").GetProperty("mode").GetString());
        }
    }

    [Fact]
    public async Task SiegeMode_RejectsInvalidSettings()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration(SiegeModeSettingsConstants.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.OK, new ApiResponse<ConfigurationDto>(
                Fixture<ConfigurationDto>(new { Namespace = SiegeModeSettingsConstants.Namespace, Configuration = "{" }))));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/siege-mode");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task SiegeMode_UsesContractDefaultsWhenGlobalConfigurationIsMissing()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GlobalConfigurations.V1.GetConfiguration(SiegeModeSettingsConstants.Namespace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConfigurationDto>(HttpStatusCode.NotFound));
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, null, 0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))
                { Pagination = new ApiPagination(5, 0, 0, 200) }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/siege-mode");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(240, body.GetProperty("defaultDurationMinutes").GetInt32());
        Assert.Empty(body.GetProperty("servers").EnumerateArray());
        Assert.Equal("Portal Test User", body.GetProperty("portalUser").GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("https://test.xtremeidiots.com", true)]
    [InlineData("https://www.xtremeidiots.com", true)]
    [InlineData("https://example.invalid", false)]
    public async Task PlayerClaimCode_PreflightAllowsJsonAndSessionAntiforgeryHeaderOnlyFromForumOrigins(string origin, bool allowed)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var session = await host.Client.GetAsync("/api/integration/v1/session");
        var tokens = await session.Content.ReadFromJsonAsync<JsonElement>();
        var headerName = tokens.GetProperty("headerName").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/integration/v1/player-links/claim-code");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", $"content-type, {headerName}");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(origins!));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
            var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
            Assert.Contains("content-type", allowedHeaders, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(headerName, allowedHeaders, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Headers"));
        }
    }

    [Fact]
    public async Task PlayerClaimCode_IssuesApprovalCodeForCurrentProfile()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var profileId = Guid.NewGuid();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(Fixture<UserProfileDto>(new { UserProfileId = profileId }))));
        repository.Setup(r => r.ConnectedPlayers.V1.IssueConnectedPlayerClaimCode(
                It.Is<ActivateConnectedPlayerActivationCodeDto>(dto => dto.UserProfileId == profileId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<ConnectedPlayerActivationCodeDto>(HttpStatusCode.OK,
                new ApiResponse<ConnectedPlayerActivationCodeDto>(Fixture<ConnectedPlayerActivationCodeDto>(new
                {
                    UserProfileId = profileId,
                    Code = "ABC123",
                    ActivatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                }))));
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/session");
        sessionRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var session = await host.Client.SendAsync(sessionRequest);
        var tokens = await session.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = Assert.Single(session.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/integration/v1/player-links/claim-code");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(tokens.GetProperty("headerName").GetString()!, tokens.GetProperty("requestVerificationToken").GetString()!);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ABC123", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("userProfileId", out _));
        repository.Verify(r => r.ConnectedPlayers.V1.IssueConnectedPlayerClaimCode(
            It.Is<ActivateConnectedPlayerActivationCodeDto>(dto => dto.UserProfileId == profileId), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.ConnectedPlayers.V1.ActivateConnectedPlayerActivationCode(
            It.IsAny<ActivateConnectedPlayerActivationCodeDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("decline")]
    [InlineData("unlink")]
    public async Task PlayerLinkMutation_UsesSessionProfileAndRequiresAntiforgery(string action)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var profileId = Guid.NewGuid();
        var id = Guid.NewGuid();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(
                Fixture<UserProfileDto>(new { UserProfileId = profileId }))));
        var claimResponse = new ApiResult<ConnectedPlayerClaimDto>(HttpStatusCode.OK, new ApiResponse<ConnectedPlayerClaimDto>(new ConnectedPlayerClaimDto()));
        repository.Setup(r => r.ConnectedPlayers.V1.ApproveConnectedPlayerClaim(profileId, id, It.IsAny<CancellationToken>())).ReturnsAsync(claimResponse);
        repository.Setup(r => r.ConnectedPlayers.V1.DeclineConnectedPlayerClaim(profileId, id, It.IsAny<CancellationToken>())).ReturnsAsync(claimResponse);
        repository.Setup(r => r.ConnectedPlayers.V1.UnlinkOwnConnectedPlayer(profileId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK, new ApiResponse()));
        var path = action == "unlink" ? $"/api/integration/v1/player-links/linked-players/{id}"
            : $"/api/integration/v1/player-links/claims/{id}/{action}";
        var method = action == "unlink" ? HttpMethod.Delete : HttpMethod.Post;

        using var rejected = new HttpRequestMessage(method, path);
        rejected.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var rejectedResponse = await host.Client.SendAsync(rejected);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/session");
        sessionRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var session = await host.Client.SendAsync(sessionRequest);
        var tokens = await session.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = Assert.Single(session.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        request.Headers.Add("Origin", "https://test.xtremeidiots.com");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(tokens.GetProperty("headerName").GetString()!, tokens.GetProperty("requestVerificationToken").GetString()!);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        repository.Verify(r => r.ConnectedPlayers.V1.ApproveConnectedPlayerClaim(profileId, id, It.IsAny<CancellationToken>()), action == "approve" ? Times.Once : Times.Never);
        repository.Verify(r => r.ConnectedPlayers.V1.DeclineConnectedPlayerClaim(profileId, id, It.IsAny<CancellationToken>()), action == "decline" ? Times.Once : Times.Never);
        repository.Verify(r => r.ConnectedPlayers.V1.UnlinkOwnConnectedPlayer(profileId, id, It.IsAny<CancellationToken>()), action == "unlink" ? Times.Once : Times.Never);
    }

    [Fact]
    public async Task PendingPlayerClaims_ExposesServerContextWithoutProfileId()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(Fixture<UserProfileDto>(new { UserProfileId = profileId }))));
        repository.Setup(r => r.ConnectedPlayers.V1.GetPendingConnectedPlayerClaims(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection([new ConnectedPlayerClaimDto { ClaimId = claimId, UserProfileId = profileId, Username = "Candidate", GameServerName = null }]));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/player-links/claims");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var claim = Assert.Single(body.GetProperty("claims").EnumerateArray());
        Assert.Equal(claimId, claim.GetProperty("claimId").GetGuid());
        Assert.Equal(JsonValueKind.Null, claim.GetProperty("gameServerName").ValueKind);
        Assert.False(claim.TryGetProperty("userProfileId", out _));
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task PendingPlayerClaims_PaginatesWithoutLosingRemainingClaims()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var profileId = Guid.NewGuid();
        var claims = Enumerable.Range(0, 101).Select(_ => new ConnectedPlayerClaimDto { ClaimId = Guid.NewGuid() }).ToArray();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(Fixture<UserProfileDto>(new { UserProfileId = profileId }))));
        repository.Setup(r => r.ConnectedPlayers.V1.GetPendingConnectedPlayerClaims(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(claims));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/player-links/claims?skip=100&take=100");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(101, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, body.GetProperty("skip").GetInt32());
        Assert.Equal(100, body.GetProperty("take").GetInt32());
        Assert.Equal(claims[100].ClaimId, Assert.Single(body.GetProperty("claims").EnumerateArray()).GetProperty("claimId").GetGuid());
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task PendingPlayerClaims_RejectsInvalidPagination(int skip, int take)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/integration/v1/player-links/claims?skip={skip}&take={take}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LinkedPlayers_UsesAuthenticatedPortalProfileAndReturnsOwnLinks()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var userProfileId = Guid.NewGuid();
        var connectedPlayerProfileId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(
                Fixture<UserProfileDto>(new { UserProfileId = userProfileId, ArchivedAtUtc = (DateTime?)null }))));
        repository.Setup(r => r.ConnectedPlayers.V1.GetConnectedPlayersByUserProfile(userProfileId, 10, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConnectedPlayerDto>>(new CollectionModel<ConnectedPlayerDto>(
                [
                    Fixture<ConnectedPlayerDto>(new
                    {
                        ConnectedPlayerProfileId = connectedPlayerProfileId,
                        PlayerId = playerId,
                        UserProfileId = userProfileId,
                        GameType = GameType.CallOfDuty4,
                        Username = "member-player",
                        LinkMethod = ConnectedPlayerLinkMethod.ActivationCode,
                        LinkedAtUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
                        IsActive = true
                    })
                ]))
                {
                    Pagination = new ApiPagination(totalCount: 1, filteredCount: 1, skip: 10, top: 20)
                }));

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/integration/v1/player-links/linked-players?skip=10&take=20&userProfileId=99999999-9999-9999-9999-999999999999");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        request.Headers.Add("Origin", "https://test.xtremeidiots.com");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("https://test.xtremeidiots.com", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, body.GetProperty("skip").GetInt32());
        Assert.Equal(20, body.GetProperty("take").GetInt32());
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(connectedPlayerProfileId, item.GetProperty("connectedPlayerProfileId").GetGuid());
        Assert.Equal(playerId, item.GetProperty("playerId").GetGuid());
        Assert.Equal("CallOfDuty4", item.GetProperty("gameType").GetString());
        Assert.Equal("member-player", item.GetProperty("username").GetString());
        Assert.Equal("ActivationCode", item.GetProperty("linkMethod").GetString());
        Assert.True(item.GetProperty("isActive").GetBoolean());
        Assert.False(item.TryGetProperty("userProfileId", out _));
        repository.Verify(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.ConnectedPlayers.V1.GetConnectedPlayersByUserProfile(userProfileId, 10, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkedPlayers_RequiresPortalAuthentication()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var response = await host.Client.GetAsync("/api/integration/v1/player-links/linked-players");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Verify(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkedPlayers_ReturnsUnavailableWhenRepositoryFails()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var userProfileId = Guid.NewGuid();
        repository.Setup(r => r.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(
                Fixture<UserProfileDto>(new { UserProfileId = userProfileId, ArchivedAtUtc = (DateTime?)null }))));
        repository.Setup(r => r.ConnectedPlayers.V1.GetConnectedPlayersByUserProfile(userProfileId, 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConnectedPlayerDto>>(HttpStatusCode.ServiceUnavailable));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/player-links/linked-players");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task LinkedPlayers_RejectsInvalidPagination(int skip, int take)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/integration/v1/player-links/linked-players?skip={skip}&take={take}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.Moderator);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(TestPrincipalProfiles.MapRotationReader, HttpStatusCode.Forbidden)]
    public async Task ServerAdmin_RequiresAdminRead(string? profile, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/server-admin");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>()).Verify(r =>
            r.GameServers.V1.GetGameServers(It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), GameServerFilter.AgentEnabled,
                0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.GameAdmin, true, true, true)]
    [InlineData(TestPrincipalProfiles.Cod4xLifecycleManager, true, false, false)]
    public async Task ServerAdmin_UsesDestinationPoliciesForEachAction(string profile, bool chat4, bool maps4, bool chat5)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var id4 = Guid.NewGuid();
        var id5 = Guid.NewGuid();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GameServers.V1.GetGameServers(It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(),
                GameServerFilter.AgentEnabled, 0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[]
            {
                Fixture<GameServerDto>(new { GameServerId = id4, GameType = GameType.CallOfDuty4, Title = "Configured", Hostname = "server.example", QueryPort = 28960 }),
                Fixture<GameServerDto>(new { GameServerId = id5, GameType = GameType.CallOfDuty5, Title = "Second" })
            }));
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[]
            {
                Fixture<GameServerLiveStatusDto>(new { ServerId = id4, Title = "^1Live title", CurrentPlayers = 4, MaxPlayers = 20, Map = "mp_test", Mod = "testmod" })
            }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/server-admin");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        request.Headers.Add("Origin", "https://test.xtremeidiots.com");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("https://test.xtremeidiots.com", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("servers").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("Live title", rows[0].GetProperty("title").GetString());
        Assert.Equal("server.example", rows[0].GetProperty("hostname").GetString());
        Assert.Equal(28960, rows[0].GetProperty("queryPort").GetInt32());
        Assert.Equal(4, rows[0].GetProperty("currentPlayers").GetInt32());
        Assert.Equal(20, rows[0].GetProperty("maxPlayers").GetInt32());
        Assert.Equal("mp_test", rows[0].GetProperty("currentMap").GetString());
        Assert.Equal("testmod", rows[0].GetProperty("mod").GetString());
        Assert.Equal($"/ServerAdmin/ServerDetail/{id4}", rows[0].GetProperty("consoleUrl").GetString());
        Assert.Equal(chat4 ? $"/ServerAdmin/ServerChatLog/{id4}" : null, rows[0].GetProperty("chatLogUrl").GetString());
        Assert.Equal(maps4 ? $"/MapManager/Manage/{id4}" : null, rows[0].GetProperty("manageMapsUrl").GetString());
        Assert.Equal(chat5 ? $"/ServerAdmin/ServerChatLog/{id5}" : null, rows[1].GetProperty("chatLogUrl").GetString());
        Assert.Null(rows[1].GetProperty("manageMapsUrl").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("currentPlayers").ValueKind);

        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/ServerAdmin");
        pageRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var pageResponse = await host.Client.SendAsync(pageRequest);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadAsStringAsync();
        Assert.Contains("Console</span>", page);
        Assert.Equal(chat4, page.Contains($"server-{id4}-view-chatlog", StringComparison.Ordinal));
        Assert.Equal(maps4, page.Contains($"server-{id4}-manage-maps", StringComparison.Ordinal));
        Assert.Equal(chat5, page.Contains($"server-{id5}-view-chatlog", StringComparison.Ordinal));
        Assert.DoesNotContain($"server-{id5}-manage-maps", page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerAdmin_ReturnsUnavailableWhenRequiredDataFails(bool failLive)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(r => r.GameServers.V1.GetGameServers(It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(),
                GameServerFilter.AgentEnabled, 0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failLive ? OkCollection(Array.Empty<GameServerDto>()) :
                new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.ServiceUnavailable));
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.ServiceUnavailable));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/server-admin");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(TestPrincipalProfiles.MapRotationReader, 0)]
    [InlineData(TestPrincipalProfiles.Moderator, 2)]
    public async Task PlayerMap_SparseLiveLocationsArePublicButNamesRequirePermission(string? profile, int expectedPlayers)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var serverId = Guid.NewGuid();
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(
                [Fixture<GameServerDto>(new { GameServerId = serverId, GameType = GameType.CallOfDuty4, ServerListEnabled = true })]))
                { Pagination = new ApiPagination(1, 1, 0, 200) }));
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection([Fixture<GameServerLiveStatusDto>(new { ServerId = serverId, IsOnline = true, CurrentPlayers = 3 })]));
        repository.Setup(r => r.LiveStatus.V1.GetGameServerLivePlayers(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[]
            {
                Fixture<LivePlayerDto>(new { Name = "synthetic-1", GeoIntelligence = new { Latitude = 52.1, Longitude = -1.2 } }),
                Fixture<LivePlayerDto>(new { Name = "synthetic-2", GeoIntelligence = new { Latitude = 52.2, Longitude = -1.3 } }),
                Fixture<LivePlayerDto>(new { Name = "no-location" })
            }));
        repository.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(null, null, It.IsAny<DateTime?>(),
                RecentPlayersFilter.GeoLocated, 0, 200, RecentPlayersOrder.TimestampDesc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(Array.Empty<RecentPlayerDto>()));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/player-map");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var points = body.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(2, points.Length);
        Assert.Equal(52.1, points[0].GetProperty("lat").GetDouble());
        Assert.Equal(-1.2, points[0].GetProperty("lng").GetDouble());
        Assert.Equal(52.2, points[1].GetProperty("lat").GetDouble());
        Assert.Equal(-1.3, points[1].GetProperty("lng").GetDouble());
        Assert.All(points, point =>
        {
            Assert.Equal(1, point.GetProperty("count").GetInt32());
            Assert.False(point.TryGetProperty("name", out _));
        });
        var players = body.GetProperty("players").EnumerateArray().ToArray();
        Assert.Equal(expectedPlayers, players.Length);
        Assert.All(players, player => Assert.Equal("live", player.GetProperty("activity").GetString()));
        if (expectedPlayers > 0)
            Assert.Equal(points[0].GetProperty("lat").GetDouble(), players[0].GetProperty("lat").GetDouble());
        else
            Assert.DoesNotContain("synthetic-", body.GetRawText());
        Assert.DoesNotContain("no-location", body.GetRawText());
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(TestPrincipalProfiles.MapRotationReader, 0)]
    [InlineData(TestPrincipalProfiles.Moderator, 10)]
    public async Task PlayerMap_ProtectsNamesAndPrivateServersAndPrefersKnownLivePlayers(string? profile, int namedCount)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var publicId = Guid.NewGuid();
        var privateId = Guid.NewGuid();
        var otherGameId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var recentPlayerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        const string privateName = "private-player";
        const string ip = "192.0.2.7";
        repository.Setup(r => r.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(
                [
                    Fixture<GameServerDto>(new { GameServerId = publicId, GameType = GameType.CallOfDuty4, ServerListEnabled = true }),
                    Fixture<GameServerDto>(new { GameServerId = otherGameId, GameType = GameType.CallOfDuty5, ServerListEnabled = true }),
                    Fixture<GameServerDto>(new { GameServerId = privateId, GameType = GameType.CallOfDuty4, ServerListEnabled = false })
                ]))
                { Pagination = new ApiPagination(5, 3, 0, 200) }));
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[] { publicId, privateId }.Select(id =>
                Fixture<GameServerLiveStatusDto>(new { ServerId = id, IsOnline = true, CurrentPlayers = 4 }))));
        repository.Setup(r => r.LiveStatus.V1.GetGameServerLivePlayers(publicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[] { "known", "anonymous-id-1", "anonymous-id-2", "no-coordinates", "sparse-live-1", "sparse-live-2", "sparse-live-3" }.Select((name, i) =>
                Fixture<LivePlayerDto>(new
                {
                    Name = name,
                    PlayerId = i == 0 ? playerId : (Guid?)null,
                    IpAddress = ip,
                    GeoIntelligence = new { Latitude = i switch { 3 => (double?)null, 4 or 5 => 42.1, 6 => 32.1, _ => 52.1 }, Longitude = -1.2 }
                }))));
        repository.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(null, null, It.IsAny<DateTime?>(),
                RecentPlayersFilter.GeoLocated, 0, 200, RecentPlayersOrder.TimestampDesc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[]
            {
                Recent(publicId, playerId, "matched-live"), Recent(publicId, null, "unmatched-recent"),
                Recent(publicId, null, "unmatched-recent-2"), Recent(privateId, null, privateName),
                Recent(publicId, null, "unmatched-recent-3"),
                Recent(publicId, recentPlayerId, "older-repeat", 32.1, now.AddMinutes(-3)),
                Recent(publicId, recentPlayerId, "latest-repeat", 42.1, now.AddMinutes(-1)),
                Recent(publicId, recentPlayerId, "older-repeat-2", 42.1, now.AddMinutes(-2)),
                Recent(publicId, recentPlayerId, "older-repeat-3", 42.1, now.AddMinutes(-3)),
                Recent(otherGameId, Guid.NewGuid(), "different-game"),
                Recent(otherGameId, Guid.NewGuid(), "different-game-2"),
                Recent(otherGameId, Guid.NewGuid(), "different-game-3")
            }));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/player-map");
        request.Headers.Add("Origin", "https://test.xtremeidiots.com");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("https://test.xtremeidiots.com", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var points = body.GetProperty("points").EnumerateArray().ToArray();
        Assert.Equal(13, points.Length);
        Assert.Equal(6, points.Count(point => point.GetProperty("activity").GetString() == "live"));
        Assert.Equal(7, points.Count(point => point.GetProperty("activity").GetString() == "recentlySeen"));
        Assert.All(points, point => Assert.Equal(1, point.GetProperty("count").GetInt32()));
        Assert.Equal(3, points.Count(point => point.GetProperty("gameType").GetString() == "CallOfDuty5"));
        Assert.Equal(namedCount, body.GetProperty("players").GetArrayLength());
        var json = body.GetRawText();
        Assert.DoesNotContain(ip, json);
        Assert.DoesNotContain(playerId.ToString(), json);
        Assert.DoesNotContain(privateName, json);
        Assert.DoesNotContain("\"matched-live\"", json);
        Assert.DoesNotContain("different-game", json);
        if (namedCount > 0)
        {
            var latest = Assert.Single(body.GetProperty("players").EnumerateArray(),
                player => player.GetProperty("name").GetString() == "latest-repeat");
            Assert.Equal(42.1, latest.GetProperty("lat").GetDouble());
            Assert.Equal("recentlySeen", latest.GetProperty("activity").GetString());
            Assert.DoesNotContain("older-repeat", json);
        }

        repository.Verify(r => r.LiveStatus.V1.GetGameServerLivePlayers(privateId, It.IsAny<CancellationToken>()), Times.Never);

        static RecentPlayerDto Recent(Guid server, Guid? player, string name, double lat = 52.1, DateTime? timestamp = null)
        {
            return Fixture<RecentPlayerDto>(new { GameServerId = server, PlayerId = player, Name = name, Lat = lat, Long = -1.2, IpAddress = ip, Timestamp = timestamp ?? DateTime.UtcNow });
        }
    }

    private static ApiResult<CollectionModel<T>> OkCollection<T>(IEnumerable<T> items) where T : class
    {
        return new(HttpStatusCode.OK, new ApiResponse<CollectionModel<T>>(new CollectionModel<T>(items)));
    }

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData(TestPrincipalProfiles.ChatLogReader, false, false, false)]
    [InlineData(TestPrincipalProfiles.Cod4xLifecycleManager, false, true, false)]
    [InlineData(TestPrincipalProfiles.Moderator, true, true, false)]
    [InlineData(TestPrincipalProfiles.GameAdmin, true, true, true)]
    public async Task Session_ReportsLandingPageCapabilities(string? profile, bool players, bool serverAdmin, bool chatLogs)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/session");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(profile is not null, body.GetProperty("authenticated").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("requestVerificationToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("headerName").GetString()));
        Assert.Equal(4, body.EnumerateObject().Count());
        var capabilities = body.GetProperty("capabilities");
        Assert.Equal(3, capabilities.EnumerateObject().Count());
        Assert.Equal(players, capabilities.GetProperty("players").GetBoolean());
        Assert.Equal(serverAdmin, capabilities.GetProperty("serverAdmin").GetBoolean());
        Assert.Equal(chatLogs, capabilities.GetProperty("chatLogs").GetBoolean());
    }

    [Theory]
    [InlineData("https://test.xtremeidiots.com", true)]
    [InlineData("https://www.xtremeidiots.com", true)]
    [InlineData("https://evil.xtremeidiots.com", false)]
    [InlineData("http://test.xtremeidiots.com", false)]
    public async Task Preflight_OnlyAllowsExactForumOrigins(string origin, bool allowed)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/integration/v1/rotations");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(origins!));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    [Fact]
    public async Task Rotations_PreservesObservedHistoryIndicator()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto
                {
                    Servers = [new() { GameServerId = Guid.NewGuid(), Title = "Server", IsObservedHistory = true }]
                })));

        using var response = await host.Client.GetAsync("/api/integration/v1/rotations");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("servers")[0].GetProperty("isObservedHistory").GetBoolean());
        Assert.False(body.TryGetProperty("telemetryProperties", out _));
    }

    [Fact]
    public async Task Maps_ReturnsPublicProjectionAndPassesPagingAndSearch()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var id = Guid.NewGuid();
        var map = Newtonsoft.Json.JsonConvert.DeserializeObject<MapDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { MapId = id, GameType = GameType.CallOfDuty4, MapName = "mp_test" }))!;
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.Maps.V1.GetMaps(GameType.CallOfDuty4, null, null, "test", 10, 5,
                MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([map]))));

        using var response = await host.Client.GetAsync("/api/integration/v1/maps?gameType=CallOfDuty4&search=test&skip=10&take=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, body.GetProperty("skip").GetInt32());
        Assert.Equal(5, body.GetProperty("take").GetInt32());
        var item = body.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("mp_test", item.GetProperty("name").GetString());
        Assert.Equal(4, item.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("?skip=-1")]
    [InlineData("?take=101")]
    [InlineData("?gameType=99999")]
    public async Task Maps_RejectsInvalidPagingAndGameType(string query)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var response = await host.Client.GetAsync("/api/integration/v1/maps" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(TestPrincipalProfiles.MapRotationReader, HttpStatusCode.Forbidden)]
    public async Task Moderation_RequiresExistingReadPolicy(string? profile, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/moderation");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>()).Verify(repository =>
            repository.AdminActions.V1.GetAdminActions(It.IsAny<GameType?>(), null, null, null,
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AdminActionOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.SeniorAdmin, true)]
    [InlineData(TestPrincipalProfiles.Moderator, false)]
    public async Task Moderation_OnlyLinksToActionsTheCallerCanEdit(string profile, bool canEdit)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var id = Guid.NewGuid();
        var action = Fixture<AdminActionDto>(new
        {
            AdminActionId = id,
            Type = AdminActionType.Ban,
            Player = new { GameType = GameType.CallOfDuty4 }
        });
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(r => r.AdminActions.V1.GetAdminActions(null, null, null, null, 0, 25,
                AdminActionOrder.CreatedDesc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OkCollection(new[] { action }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/moderation");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(canEdit ? $"/AdminActions/Edit/{id}" : null, body.GetProperty("items")[0].GetProperty("actionUrl").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Banners_ReportsIncompleteConfigurationInsteadOfPartialSuccess(bool complete)
    {
        var id = Guid.NewGuid();
        var settings = new Mock<IGameServerConfigurationReadService>();
        var configs = new Dictionary<Guid, GameServerReadSettings>();
        if (complete)
        {
            var transport = new FileTransportReadSettings(null, 0, null, null);
            configs[id] = new GameServerReadSettings(null, "<b>Server</b>", transport, transport);
        }

        settings.Setup(service => service.GetAsync(It.IsAny<IEnumerable<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IGameServerConfigurationReadService>();
            services.AddSingleton(settings.Object);
        });
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { GameServerId = id }))!;
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))));

        using var response = await host.Client.GetAsync("/api/integration/v1/banners");

        Assert.Equal(complete ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (complete)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("<b>Server</b>", body.GetProperty("banners")[0].GetString());
        }
    }

    [Fact]
    public async Task Banners_ReadsAllServerPages()
    {
        var servers = Enumerable.Range(0, 51).Select(_ => Fixture<GameServerDto>(new { GameServerId = Guid.NewGuid() })).ToArray();
        var transport = new FileTransportReadSettings(null, 0, null, null);
        var configs = servers.ToDictionary(server => server.GameServerId,
            _ => new GameServerReadSettings(null, "<b>Server</b>", transport, transport));
        var settings = new Mock<IGameServerConfigurationReadService>();
        settings.Setup(service => service.GetAsync(It.IsAny<IEnumerable<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IGameServerConfigurationReadService>();
            services.AddSingleton(settings.Object);
        });
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers.Take(50)))
                { Pagination = new ApiPagination(51, 51, 0, 50) }));
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                50, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers.Skip(50)))
                { Pagination = new ApiPagination(51, 51, 50, 50) }));

        using var response = await host.Client.GetAsync("/api/integration/v1/banners");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(51, body.GetProperty("banners").GetArrayLength());
    }

    [Theory]
    [InlineData(0, "populated")]
    [InlineData(24, "failed")]
    [InlineData(48, "failed")]
    [InlineData(24, "empty")]
    [InlineData(48, "empty")]
    [InlineData(24, "missingData")]
    [InlineData(48, "missingItems")]
    public async Task Stats_CombinesGroupedCountsOrReportsUnavailable(int windowHours, string scenario)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var id = Guid.NewGuid();
        var server = Fixture<GameServerDto>(new { GameServerId = id, GameType = GameType.CallOfDuty4 });
        var live = Fixture<GameServerLiveStatusDto>(new { ServerId = id, CurrentPlayers = 7 });
        var dashboard = Fixture<DashboardSummaryDto>(new { TotalPlayersOnline = 7, UnclaimedBanCount = 2, OpenReportCount = 3 });
        repository.Setup(client => client.Dashboard.V1.GetDashboardSummary(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<DashboardSummaryDto>(HttpStatusCode.OK, new ApiResponse<DashboardSummaryDto>(dashboard)));
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, null, 0, 200,
                GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))
                { Pagination = new ApiPagination(1, 1, 0, 200) }));
        repository.Setup(client => client.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerLiveStatusDto>>(new CollectionModel<GameServerLiveStatusDto>([live]))));
        repository.Setup(client => client.Players.V1.GetPlayers(null, null, null, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([]))
                { Pagination = new ApiPagination(125, 125, 0, 1) }));
        repository.Setup(client => client.PlayerAnalyticsV2.V1.GetByGame(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, CancellationToken _) =>
            {
                var hours = (to - from).TotalHours;
                Assert.Contains(hours, new[] { 24d, 48d });
                if (hours == windowHours)
                    return scenario switch
                    {
                        "failed" => new ApiResult<PlayersByGameDto>(HttpStatusCode.ServiceUnavailable),
                        "empty" => new ApiResult<PlayersByGameDto>(HttpStatusCode.OK, new ApiResponse<PlayersByGameDto>(new PlayersByGameDto())),
                        "missingData" => new ApiResult<PlayersByGameDto>(HttpStatusCode.OK, new ApiResponse<PlayersByGameDto>()),
                        "missingItems" => new ApiResult<PlayersByGameDto>(HttpStatusCode.OK, new ApiResponse<PlayersByGameDto>(
                            Fixture<PlayersByGameDto>(new { Items = (object?)null }))),
                        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
                    };

                return new ApiResult<PlayersByGameDto>(HttpStatusCode.OK, new ApiResponse<PlayersByGameDto>(
                    Fixture<PlayersByGameDto>(new
                    {
                        Items = new[]
                        {
                            new { GameType = GameType.CallOfDuty4, ActivePlayers = hours == 24 ? 6 : 12 },
                            new { GameType = GameType.CallOfDuty5, ActivePlayers = hours == 24 ? 4 : 8 }
                        }
                    })));
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/stats");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.SendAsync(request);
        repository.Verify(client => client.PlayerAnalyticsV2.V1.GetOverview(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
        if (scenario is "failed" or "missingData" or "missingItems")
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            return;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asOfUtc = body.GetProperty("asOfUtc").GetDateTime();
        repository.Verify(client => client.PlayerAnalyticsV2.V1.GetByGame(asOfUtc.AddHours(-24), asOfUtc,
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(client => client.PlayerAnalyticsV2.V1.GetByGame(asOfUtc.AddHours(-48), asOfUtc,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(125, body.GetProperty("trackedPlayers").GetInt32());
        Assert.Equal(windowHours == 24 ? 0 : 10, body.GetProperty("activePlayers24h").GetInt32());
        Assert.Equal(windowHours == 48 ? 0 : 20, body.GetProperty("activePlayers48h").GetInt32());
        Assert.Equal(7, body.GetProperty("onlineByGame")[0].GetProperty("playersOnline").GetInt32());
        Assert.Equal(2, body.GetProperty("unclaimedBans").GetInt32());
    }

    [Fact]
    public async Task Activity_UsesExistingPolicyAndPassesPagingToActivityService()
    {
        var activity = new Mock<IActivityLogService>();
        activity.Setup(service => service.QueryEventsAsync(TimeSpan.FromHours(24), It.IsAny<IReadOnlyList<ActivityLogCategory>>(),
                It.IsAny<IReadOnlyList<string>>(), "audit", 10, 5, "timestamp", "desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityLogQueryResult
            {
                TotalCount = 20,
                FilteredCount = 1,
                Entries = [new ActivityLogEntry { EventName = "SyntheticEvent", Category = "Test" }]
            });
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton(activity.Object);
        });
        using var denied = await host.Client.GetAsync("/api/integration/v1/activity");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/activity?search=audit&skip=10&take=5");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, body.GetProperty("totalCount").GetInt32());
        Assert.Equal("SyntheticEvent", body.GetProperty("items")[0].GetProperty("eventName").GetString());
    }

    private static T Fixture<T>(object value)
        => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(Newtonsoft.Json.JsonConvert.SerializeObject(value))!;
}
