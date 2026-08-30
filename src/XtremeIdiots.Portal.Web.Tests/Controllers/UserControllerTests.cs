using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using MX.Observability.ApplicationInsights.Auditing.Models;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class UserControllerTests
{
#pragma warning disable IDE0028
    public static TheoryData<string, bool> GlobalAdminLegacyCleanupScenarios => new()
    {
        { UserProfileClaimType.SeniorAdmin, true },
        { UserProfileClaimType.SeniorAdmin, false },
        { UserProfileClaimType.Webmaster, true },
        { UserProfileClaimType.Webmaster, false }
    };

    public static TheoryData<string, string[]> HeadAdminLogoutAllowedTargets => new()
    {
        { "99999", Array.Empty<string>() },
        { "99999", new[] { UserProfileClaimType.Moderator } },
        { "99999", new[] { UserProfileClaimType.GameAdmin } },
        { "99999", new[] { UserProfileClaimType.HeadAdmin } },
        { "12345", new[] { UserProfileClaimType.HeadAdmin } }
    };

    public static TheoryData<string> ProtectedTargetClaimTypes => new()
    {
        UserProfileClaimType.SeniorAdmin,
        UserProfileClaimType.Webmaster
    };

    public static TheoryData<string, string> GlobalAdminLogoutScenarios => new()
    {
        { UserProfileClaimType.SeniorAdmin, UserProfileClaimType.SeniorAdmin },
        { UserProfileClaimType.SeniorAdmin, UserProfileClaimType.Webmaster },
        { UserProfileClaimType.Webmaster, UserProfileClaimType.SeniorAdmin },
        { UserProfileClaimType.Webmaster, UserProfileClaimType.Webmaster }
    };
#pragma warning restore IDE0028

    private readonly Mock<IAuthorizationService> mockAuthorizationService = new();
    private readonly Mock<IRepositoryApiClient> mockRepositoryApiClient = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    private readonly Mock<UserManager<IdentityUser>> mockUserManager;
    private readonly TelemetryClient telemetryClient = new(new TelemetryConfiguration());
    private readonly Mock<ILogger<UserController>> mockLogger = new();
    private readonly Mock<IConfiguration> mockConfiguration = new();
    private readonly Mock<IAuditLogger> mockAuditLogger = new();

    public UserControllerTests()
    {
        var mockUserStore = new Mock<IUserStore<IdentityUser>>();
        mockUserManager = new Mock<UserManager<IdentityUser>>(
            mockUserStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    [Fact]
    public async Task CreateUserClaim_Cod5GamePermission_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId);
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);

        var result = await sut.CreateUserClaim(profileId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString());

        AssertRedirectsToManageProfile(result, profileId);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.CreateUserProfileClaim(
            profileId,
            It.Is<List<CreateUserProfileClaimDto>>(claims =>
                claims.Count == 1 &&
                claims.Single().ClaimType == AdditionalPermission.GameServers_Write &&
                claims.Single().ClaimValue == GameType.CallOfDuty5.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserClaim_Cod5ServerPermission_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId);
        SetupGameServer(serverId, GameType.CallOfDuty5);
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);

        var result = await sut.CreateUserClaim(profileId, AdditionalPermission.GameServers_Credentials_Rcon_Read, serverId.ToString());

        AssertRedirectsToManageProfile(result, profileId);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.CreateUserProfileClaim(
            profileId,
            It.Is<List<CreateUserProfileClaimDto>>(claims =>
                claims.Count == 1 &&
                claims.Single().ClaimValue == serverId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserClaim_Cod4GamePermission_Denied_DoesNotMutate()
    {
        var profileId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId);
        SetupAuthorizationFailure(GameType.CallOfDuty4, AuthPolicies.Users_ManageClaims);

        var result = await sut.CreateUserClaim(profileId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty4.ToString());

        Assert.IsType<UnauthorizedResult>(result);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.CreateUserProfileClaim(
            It.IsAny<Guid>(),
            It.IsAny<List<CreateUserProfileClaimDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task CreateUserClaim_Cod4ServerPermission_Denied_DoesNotMutate()
    {
        var profileId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId);
        SetupGameServer(serverId, GameType.CallOfDuty4);
        SetupAuthorizationFailure(GameType.CallOfDuty4, AuthPolicies.Users_ManageClaims);

        var result = await sut.CreateUserClaim(profileId, AdditionalPermission.GameServers_Credentials_Rcon_Read, serverId.ToString());

        Assert.IsType<UnauthorizedResult>(result);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.CreateUserProfileClaim(
            It.IsAny<Guid>(),
            It.IsAny<List<CreateUserProfileClaimDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task RemoveUserClaim_Cod5GamePermission_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var identityUser = new IdentityUser { Id = "12345", UserName = "TargetUser" };
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString())], forumId: "12345");
        SetupIdentityUser(identityUser);
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);

        var result = await sut.RemoveUserClaim(profileId, claimId);

        AssertRedirectsToManageProfile(result, profileId);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.DeleteUserProfileClaim(profileId, claimId, It.IsAny<CancellationToken>()), Times.Once);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(identityUser), Times.Once);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task RemoveUserClaim_Cod5ServerPermission_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var identityUser = new IdentityUser { Id = "12345", UserName = "TargetUser" };
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, serverId.ToString())], forumId: "12345");
        SetupGameServer(serverId, GameType.CallOfDuty5);
        SetupIdentityUser(identityUser);
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);

        var result = await sut.RemoveUserClaim(profileId, claimId);

        AssertRedirectsToManageProfile(result, profileId);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.DeleteUserProfileClaim(profileId, claimId, It.IsAny<CancellationToken>()), Times.Once);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(identityUser), Times.Once);
    }

    [Fact]
    public async Task RemoveUserClaim_Cod4Permission_Denied_DoesNotMutate()
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty4.ToString())]);
        SetupAuthorizationFailure(GameType.CallOfDuty4, AuthPolicies.Users_ManageClaims);

        var result = await sut.RemoveUserClaim(profileId, claimId);

        Assert.IsType<UnauthorizedResult>(result);
        AssertNoClaimRemovalMutation();
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task RemoveUserClaim_SystemGenerated_Denied_DoesNotMutate()
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString(), systemGenerated: true)]);

        var result = await sut.RemoveUserClaim(profileId, claimId);

        Assert.IsType<UnauthorizedResult>(result);
        AssertNoClaimRemovalMutation();
        mockAuthorizationService.Verify(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<string>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveUserClaim_LegacyScope_DeniedToHeadAdmin_WhenDeletedServerOrUnknownValue(bool deletedServer)
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var scopeValue = deletedServer ? Guid.NewGuid().ToString() : "legacy-scope";
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Write, scopeValue)]);

        if (deletedServer)
        {
            mockRepositoryApiClient
                .Setup(x => x.GameServers.V1.GetGameServer(Guid.Parse(scopeValue), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));
        }

        var result = await sut.RemoveUserClaim(profileId, claimId);

        Assert.IsType<UnauthorizedResult>(result);
        AssertNoClaimRemovalMutation();
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminLegacyCleanupScenarios))]
    public async Task RemoveUserClaim_LegacyScope_AllowedForGlobalAdmins(string actorClaimType, bool deletedServer)
    {
        var profileId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var scopeValue = deletedServer ? Guid.NewGuid().ToString() : "legacy-scope";
        var identityUser = new IdentityUser { Id = "12345", UserName = "TargetUser" };
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));
        SetupUserProfile(profileId, [CreateClaim(claimId, AdditionalPermission.GameServers_Write, scopeValue)], forumId: "12345");
        SetupIdentityUser(identityUser);

        if (deletedServer)
        {
            mockRepositoryApiClient
                .Setup(x => x.GameServers.V1.GetGameServer(Guid.Parse(scopeValue), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));
        }

        var result = await sut.RemoveUserClaim(profileId, claimId);

        AssertRedirectsToManageProfile(result, profileId);
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.DeleteUserProfileClaim(profileId, claimId, It.IsAny<CancellationToken>()), Times.Once);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(identityUser), Times.Once);
    }

    [Theory]
    [MemberData(nameof(HeadAdminLogoutAllowedTargets))]
    public async Task LogUserOut_HeadAdmin_SucceedsForAllowedTargets(string targetForumId, string[] targetRoleClaimTypes)
    {
        var identityUser = new IdentityUser { Id = targetForumId, UserName = "TargetUser" };
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5, forumId: "12345"));
        SetupIdentityUser(identityUser);
        SetupUsersLogOutAuthorizationSuccess();
        SetupTargetProfileByForumId(targetForumId, targetRoleClaimTypes.Select(role => CreateClaim(Guid.NewGuid(), role, GameType.CallOfDuty5.ToString())));

        var result = await sut.LogUserOut(targetForumId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserController.Index), redirect.ActionName);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(identityUser), Times.Once);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(ProtectedTargetClaimTypes))]
    public async Task LogUserOut_HeadAdmin_DeniedForProtectedTargets(string protectedRoleClaimType)
    {
        const string targetForumId = "99999";
        var identityUser = new IdentityUser { Id = targetForumId, UserName = "ProtectedTarget" };
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupIdentityUser(identityUser);
        SetupUsersLogOutAuthorizationSuccess();
        SetupTargetProfileByForumId(targetForumId, [CreateClaim(Guid.NewGuid(), protectedRoleClaimType, bool.TrueString)]);

        var result = await sut.LogUserOut(targetForumId);

        Assert.IsType<UnauthorizedResult>(result);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminLogoutScenarios))]
    public async Task LogUserOut_GlobalAdmins_CanLogOutProtectedTargets(string actorClaimType, string targetClaimType)
    {
        const string targetForumId = "99999";
        var identityUser = new IdentityUser { Id = targetForumId, UserName = "ProtectedTarget" };
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));
        SetupIdentityUser(identityUser);
        SetupUsersLogOutAuthorizationSuccess();
        SetupTargetProfileByForumId(targetForumId, [CreateClaim(Guid.NewGuid(), targetClaimType, bool.TrueString)]);

        var result = await sut.LogUserOut(targetForumId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserController.Index), redirect.ActionName);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(identityUser), Times.Once);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task LogUserOut_WhenTargetRolesCannotBeEstablished_FailsClosed()
    {
        const string targetForumId = "99999";
        var identityUser = new IdentityUser { Id = targetForumId, UserName = "TargetUser" };
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupIdentityUser(identityUser);
        SetupUsersLogOutAuthorizationSuccess();
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(targetForumId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));

        var result = await sut.LogUserOut(targetForumId);

        Assert.IsType<UnauthorizedResult>(result);
        mockUserManager.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<IdentityUser>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    private UserController CreateSut(ClaimsPrincipal? user = null)
    {
        var controller = new UserController(
            mockAuthorizationService.Object,
            mockRepositoryApiClient.Object,
            mockUserManager.Object,
            telemetryClient,
            mockLogger.Object,
            mockConfiguration.Object,
            mockAuditLogger.Object);

        var httpContext = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
        };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    private void SetupAuthorizationSuccess(GameType resourceGameType, string policy)
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object>(resource => resource is GameType && (GameType)resource == resourceGameType),
                policy))
            .ReturnsAsync(AuthorizationResult.Success());
    }

    private void SetupAuthorizationFailure(GameType resourceGameType, string policy)
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<object>(resource => resource is GameType && (GameType)resource == resourceGameType),
                policy))
            .ReturnsAsync(AuthorizationResult.Failed());
    }

    private void SetupUsersLogOutAuthorizationSuccess()
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.Users_LogOut))
            .ReturnsAsync(AuthorizationResult.Success());
    }

    private void SetupUserProfile(Guid profileId, IEnumerable<object>? claims = null, string forumId = "12345", string displayName = "Target User")
    {
        var userProfile = CreateUserProfileDto(profileId, forumId, claims ?? []);
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfile(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));
    }

    private void SetupTargetProfileByForumId(string forumId, IEnumerable<object> claims)
    {
        var userProfile = CreateUserProfileDto(Guid.NewGuid(), forumId, claims);
        mockRepositoryApiClient
            .Setup(x => x.UserProfiles.V1.GetUserProfileByXtremeIdiotsId(forumId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(userProfile)));
    }

    private void SetupGameServer(Guid serverId, GameType gameType)
    {
        var gameServer = CreateGameServerDto(serverId, gameType);
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(gameServer)));
    }

    private void SetupIdentityUser(IdentityUser user)
    {
        mockUserManager
            .Setup(x => x.FindByIdAsync(user.Id))
            .ReturnsAsync(user);
        mockUserManager
            .Setup(x => x.UpdateSecurityStampAsync(user))
            .ReturnsAsync(IdentityResult.Success);
    }

    private void AssertNoClaimRemovalMutation()
    {
        mockRepositoryApiClient.Verify(x => x.UserProfiles.V1.DeleteUserProfileClaim(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRedirectsToManageProfile(IActionResult result, Guid profileId)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UserController.ManageProfile), redirect.ActionName);
        Assert.Equal(profileId, redirect.RouteValues?["id"]);
    }

    private static ClaimsPrincipal CreateHeadAdminPrincipal(GameType gameType, string forumId = "55555")
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Head Admin"),
            new Claim(UserProfileClaimType.XtremeIdiotsId, forumId),
            new Claim(UserProfileClaimType.UserProfileId, Guid.NewGuid().ToString()),
            new Claim(UserProfileClaimType.HeadAdmin, gameType.ToString()),
        ], "TestAuth"));
    }

    private static ClaimsPrincipal CreateGlobalAdminPrincipal(string globalClaimType)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "Global Admin"),
            new Claim(UserProfileClaimType.XtremeIdiotsId, "77777"),
            new Claim(UserProfileClaimType.UserProfileId, Guid.NewGuid().ToString()),
            new Claim(globalClaimType, bool.TrueString),
        ], "TestAuth"));
    }

    private static object CreateClaim(Guid claimId, string claimType, string claimValue, bool systemGenerated = false)
    {
        return new
        {
            UserProfileClaimId = claimId,
            ClaimType = claimType,
            ClaimValue = claimValue,
            SystemGenerated = systemGenerated
        };
    }

    private static UserProfileDto CreateUserProfileDto(Guid userProfileId, string forumId, IEnumerable<object> claims)
    {
        var json = JsonConvert.SerializeObject(new
        {
            UserProfileId = userProfileId,
            XtremeIdiotsForumId = forumId,
            DisplayName = "Target User",
            Email = "target@example.invalid",
            UserProfileClaims = claims
        });

        return JsonConvert.DeserializeObject<UserProfileDto>(json)!;
    }

    private static GameServerDto CreateGameServerDto(Guid gameServerId, GameType gameType)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "Test Server",
            GameType = gameType,
            Platform = GameServerPlatform.Windows,
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = true,
            FileTransportEnabled = true,
            FileTransportType = "Ftp",
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }
}
