using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Notifications;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Portal.Web.ViewModels;

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

    public static TheoryData<string> GlobalAdminClaimTypes => new()
    {
        UserProfileClaimType.SeniorAdmin,
        UserProfileClaimType.Webmaster
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

        SetupNotificationPreferencesAuthorizationFailure();
        SetupNotificationTypes();
        SetupNotificationPreferences();
        SetupNotificationHistory();
        SetupNotificationPreferenceUpdateSuccess();
    }

    [Fact]
    public async Task ManageProfile_HeadAdminOnlyGetsRemovalControlsForOwnedResolvableClaims()
    {
        var profileId = Guid.NewGuid();
        var cod5GameClaimId = Guid.NewGuid();
        var cod5ServerClaimId = Guid.NewGuid();
        var cod4GameClaimId = Guid.NewGuid();
        var cod4ServerClaimId = Guid.NewGuid();
        var deletedServerClaimId = Guid.NewGuid();
        var unknownScopeClaimId = Guid.NewGuid();
        var systemGeneratedClaimId = Guid.NewGuid();
        var cod5ServerId = Guid.NewGuid();
        var cod4ServerId = Guid.NewGuid();
        var deletedServerId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        SetupUserProfile(profileId,
        [
            CreateClaim(cod5GameClaimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString()),
            CreateClaim(cod5ServerClaimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, cod5ServerId.ToString()),
            CreateClaim(cod4GameClaimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty4.ToString()),
            CreateClaim(cod4ServerClaimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, cod4ServerId.ToString()),
            CreateClaim(deletedServerClaimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, deletedServerId.ToString()),
            CreateClaim(unknownScopeClaimId, AdditionalPermission.GameServers_Write, "legacy-scope"),
            CreateClaim(systemGeneratedClaimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString(), systemGenerated: true)
        ]);
        SetupGameServersList(CreateGameServerDto(cod5ServerId, GameType.CallOfDuty5));
        SetupGameServer(cod5ServerId, GameType.CallOfDuty5);
        SetupGameServer(cod4ServerId, GameType.CallOfDuty4);
        SetupGameServerNotFound(deletedServerId);
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);
        SetupAuthorizationFailure(GameType.CallOfDuty4, AuthPolicies.Users_ManageClaims);

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        var claimRows = model.Claims.ToDictionary(claim => claim.UserProfileClaimId);

        Assert.True(claimRows[cod5GameClaimId].CanRemove);
        Assert.True(claimRows[cod5ServerClaimId].CanRemove);
        Assert.Equal("Test Server", claimRows[cod5ServerClaimId].ScopeDisplayValue);
        Assert.False(claimRows[cod4GameClaimId].CanRemove);
        Assert.False(claimRows[cod4ServerClaimId].CanRemove);
        Assert.False(claimRows[deletedServerClaimId].CanRemove);
        Assert.False(claimRows[unknownScopeClaimId].CanRemove);
        Assert.False(claimRows[systemGeneratedClaimId].CanRemove);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminClaimTypes))]
    public async Task ManageProfile_GlobalAdminsGetCleanupControlsForAllNonSystemClaims(string actorClaimType)
    {
        var profileId = Guid.NewGuid();
        var legacyClaimId = Guid.NewGuid();
        var deletedServerClaimId = Guid.NewGuid();
        var systemGeneratedClaimId = Guid.NewGuid();
        var deletedServerId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));

        SetupUserProfile(profileId,
        [
            CreateClaim(legacyClaimId, AdditionalPermission.GameServers_Write, "legacy-scope"),
            CreateClaim(deletedServerClaimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, deletedServerId.ToString()),
            CreateClaim(systemGeneratedClaimId, AdditionalPermission.GameServers_Write, GameType.CallOfDuty5.ToString(), systemGenerated: true)
        ]);
        SetupGameServersList();
        SetupGameServerNotFound(deletedServerId);

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        var claimRows = model.Claims.ToDictionary(claim => claim.UserProfileClaimId);

        Assert.True(claimRows[legacyClaimId].CanRemove);
        Assert.True(claimRows[deletedServerClaimId].CanRemove);
        Assert.False(claimRows[systemGeneratedClaimId].CanRemove);
    }

    [Fact]
    public async Task ManageProfile_HeadAdminLoadsNotificationDataReadOnly_UsingEffectivePreferences()
    {
        var profileId = Guid.NewGuid();
        var explicitTypeId = Guid.NewGuid();
        var defaultedTypeId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        SetupGameServersList();
        SetupUserProfile(profileId);
        SetupNotificationPreferencesAuthorizationFailure();
        SetupNotificationTypes(
            CreateNotificationType(explicitTypeId, defaultChannels: "Email"),
            CreateNotificationType(defaultedTypeId, supportsEmail: false, defaultChannels: "InSite"));
        SetupNotificationPreferences(
            CreateNotificationPreference(explicitTypeId, inSiteEnabled: false, emailEnabled: true));
        SetupNotificationHistory(
            CreateNotification(notificationId, explicitTypeId, title: "Dispatch ready", message: "Full notification message content"));

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);

        Assert.False(model.CanUpdateNotificationPreferences);
        Assert.Equal(2, model.NotificationTypes.Count);
        Assert.Equal(2, model.NotificationPreferences.Count);
        Assert.Single(model.RecentNotifications);

        var explicitPreference = Assert.Single(model.NotificationPreferences, x => x.NotificationTypeId == explicitTypeId.ToString());
        Assert.False(explicitPreference.InAppEnabled);
        Assert.True(explicitPreference.EmailEnabled);
        Assert.False(explicitPreference.ExplicitInAppEnabled);
        Assert.True(explicitPreference.ExplicitEmailEnabled);

        var defaultedPreference = Assert.Single(model.NotificationPreferences, x => x.NotificationTypeId == defaultedTypeId.ToString());
        Assert.True(defaultedPreference.InAppEnabled);
        Assert.False(defaultedPreference.EmailEnabled);
        Assert.Null(defaultedPreference.ExplicitInAppEnabled);
        Assert.Null(defaultedPreference.ExplicitEmailEnabled);

        var historyEntry = Assert.Single(model.RecentNotifications);
        Assert.Equal(notificationId, historyEntry.NotificationId);
        Assert.Equal("Dispatch ready", historyEntry.Title);
        Assert.Equal("Full notification message content", historyEntry.Message);
        Assert.Equal($"Notification {explicitTypeId}", historyEntry.NotificationType);

        mockAuthorizationService.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.IsAny<object>(),
            AuthPolicies.Users_ManageNotificationPreferences), Times.Once);
        mockRepositoryApiClient.Verify(x => x.Notifications.V1.GetNotifications(
            profileId,
            null,
            0,
            50,
            NotificationOrder.CreatedAtDesc,
            It.IsAny<CancellationToken>()), Times.Once);
        AssertNoNotificationStateMutation();
    }

    [Theory]
    [MemberData(nameof(GlobalAdminClaimTypes))]
    public async Task ManageProfile_GlobalAdminsCanUpdateNotificationPreferences(string actorClaimType)
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));

        SetupGameServersList();
        SetupUserProfile(profileId);
        SetupNotificationPreferencesAuthorizationSuccess();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: true, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeId));

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        Assert.True(model.CanUpdateNotificationPreferences);
    }

    [Fact]
    public async Task ManageProfile_WhenNotificationTypeApiFails_SurfacesVisibleNotificationError()
    {
        var profileId = Guid.NewGuid();
        var historyNotificationId = Guid.NewGuid();
        var historyNotificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        SetupGameServersList();
        SetupUserProfile(profileId);
        SetupNotificationPreferencesAuthorizationFailure();
        SetupNotificationTypesFailure();
        SetupNotificationHistory(CreateNotification(historyNotificationId, historyNotificationTypeId));

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        Assert.Equal("Notification preferences are currently unavailable. Please try again later.", model.NotificationPreferencesErrorMessage);
        Assert.Empty(model.NotificationTypes);
        Assert.Empty(model.NotificationPreferences);
        Assert.Null(model.NotificationHistoryErrorMessage);
        var historyEntry = Assert.Single(model.RecentNotifications);
        Assert.Equal(historyNotificationId, historyEntry.NotificationId);
        Assert.Equal(historyNotificationTypeId.ToString(), historyEntry.NotificationType);
    }

    [Fact]
    public async Task ManageProfile_WhenNotificationHistoryApiFails_PreferencesRemainAvailable()
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        SetupGameServersList();
        SetupUserProfile(profileId);
        SetupNotificationPreferencesAuthorizationFailure();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId, defaultChannels: "Email"));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: false, emailEnabled: true));
        SetupNotificationHistoryFailure();

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        Assert.Null(model.NotificationPreferencesErrorMessage);
        Assert.Equal("Notification history is currently unavailable. Please try again later.", model.NotificationHistoryErrorMessage);
        Assert.Single(model.NotificationTypes);
        Assert.Single(model.NotificationPreferences);
        Assert.Empty(model.RecentNotifications);
    }

    [Fact]
    public async Task ManageProfile_WhenGameServerListFails_ProfileAndNotificationsRemainAvailableAndPermissionControlsAreDisabled()
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        SetupUserProfile(profileId,
        [
            CreateClaim(claimId, AdditionalPermission.GameServers_Credentials_Rcon_Read, serverId.ToString())
        ]);
        SetupGameServersListFailure();
        SetupGameServer(serverId, GameType.CallOfDuty5);
        SetupNotificationPreferencesAuthorizationFailure();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: true, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeId));
        SetupAuthorizationSuccess(GameType.CallOfDuty5, AuthPolicies.Users_ManageClaims);

        var result = await sut.ManageProfile(profileId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserProfileViewModel>(view.Model);
        Assert.Equal("Target User", model.Profile.DisplayName);
        Assert.Null(model.NotificationPreferencesErrorMessage);
        Assert.Single(model.NotificationTypes);
        Assert.Single(model.NotificationPreferences);
        Assert.Single(model.RecentNotifications);
        Assert.Equal("Additional permission management is currently unavailable. Please try again later.", model.PermissionsErrorMessage);
        Assert.Empty(model.AssignableGameTypes);
        Assert.All(model.Claims, claim => Assert.False(claim.CanRemove));
        var assignableServers = Assert.IsAssignableFrom<SelectList>(view.ViewData["AssignableGameServersSelect"]);
        Assert.Empty(assignableServers.Items);
        mockAuthorizationService.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.Is<object>(resource => resource is GameType),
            AuthPolicies.Users_ManageClaims), Times.Never);
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

    [Fact]
    public async Task LogUserOut_WhenAuthorizationDenied_SanitizesTargetIdInAuditContext()
    {
        const string rawTargetId = "99999\r\nInjected";
        AuditEvent? capturedAuditEvent = null;
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupUsersLogOutAuthorizationFailure();
        mockAuditLogger
            .Setup(x => x.LogAudit(It.IsAny<AuditEvent>()))
            .Callback<AuditEvent>(auditEvent => capturedAuditEvent = auditEvent);

        var result = await sut.LogUserOut(rawTargetId);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.NotNull(capturedAuditEvent);
        Assert.Equal("TargetUserId:99999Injected", capturedAuditEvent.Properties["Context"]);
        Assert.DoesNotContain('\r', capturedAuditEvent.Properties["Context"]);
        Assert.DoesNotContain('\n', capturedAuditEvent.Properties["Context"]);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_HeadAdminDenied_DoesNotMutate()
    {
        var profileId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));
        SetupNotificationPreferencesAuthorizationFailure();

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId
        });

        Assert.IsType<UnauthorizedResult>(result);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            It.IsAny<Guid>(),
            It.IsAny<List<EditNotificationPreferenceDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminClaimTypes))]
    public async Task UpdateUserNotificationPreferences_GlobalAdmins_MapCheckedAndUncheckedPreferences(string actorClaimType)
    {
        var profileId = Guid.NewGuid();
        var notificationTypeOne = Guid.NewGuid();
        var notificationTypeTwo = Guid.NewGuid();
        var notificationTypeThree = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));
        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(
            CreateNotificationType(notificationTypeOne),
            CreateNotificationType(notificationTypeTwo, supportsInSite: false),
            CreateNotificationType(notificationTypeThree));
        SetupNotificationPreferences(
            CreateNotificationPreference(notificationTypeOne, inSiteEnabled: false, emailEnabled: false),
            CreateNotificationPreference(notificationTypeTwo, inSiteEnabled: false, emailEnabled: false),
            CreateNotificationPreference(notificationTypeThree, inSiteEnabled: true, emailEnabled: true));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeOne));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = notificationTypeOne.ToString(), InAppEnabled = true, EmailEnabled = true },
                new() { NotificationTypeId = notificationTypeTwo.ToString(), InAppEnabled = false, EmailEnabled = true },
                new() { NotificationTypeId = notificationTypeThree.ToString(), InAppEnabled = false, EmailEnabled = false }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            profileId,
            It.Is<List<EditNotificationPreferenceDto>>(preferences =>
                preferences.Count == 3 &&
                HasNotificationPreference(preferences, notificationTypeOne, true, true) &&
                HasNotificationPreference(preferences, notificationTypeTwo, false, true) &&
                HasNotificationPreference(preferences, notificationTypeThree, false, false)),
            It.IsAny<CancellationToken>()), Times.Once);
        AssertNoNotificationStateMutation();
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_UnchangedInheritedDefaults_AreOmittedFromUpdates()
    {
        var profileId = Guid.NewGuid();
        var inheritedTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(inheritedTypeId, defaultChannels: "Email"));
        SetupNotificationPreferences();
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), inheritedTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = inheritedTypeId.ToString(), InAppEnabled = false, EmailEnabled = true }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            It.IsAny<Guid>(),
            It.IsAny<List<EditNotificationPreferenceDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("Notification preferences are already up to date.", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_ChangedInheritedDefaults_AreSentAsExplicitOverrides()
    {
        var profileId = Guid.NewGuid();
        var inheritedTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(inheritedTypeId, defaultChannels: "Email"));
        SetupNotificationPreferences();
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), inheritedTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = inheritedTypeId.ToString(), InAppEnabled = true, EmailEnabled = false }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            profileId,
            It.Is<List<EditNotificationPreferenceDto>>(preferences =>
                preferences.Count == 1 &&
                HasNotificationPreference(preferences, inheritedTypeId, true, false)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_ExistingExplicitPreferences_RemainExplicitAndUpdateable()
    {
        var profileId = Guid.NewGuid();
        var explicitTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(explicitTypeId, defaultChannels: "Email"));
        SetupNotificationPreferences(CreateNotificationPreference(explicitTypeId, inSiteEnabled: false, emailEnabled: true));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), explicitTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = explicitTypeId.ToString(), InAppEnabled = true, EmailEnabled = true }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            profileId,
            It.Is<List<EditNotificationPreferenceDto>>(preferences =>
                preferences.Count == 1 &&
                HasNotificationPreference(preferences, explicitTypeId, true, true)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminClaimTypes))]
    public async Task UpdateUserNotificationPreferences_GlobalAdmins_AuditChangedPreferenceContext(string actorClaimType)
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        AuditEvent? capturedAuditEvent = null;
        var sut = CreateSut(CreateGlobalAdminPrincipal(actorClaimType));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId, displayName: "Target\r\nUser");
        SetupNotificationTypes(CreateNotificationType(notificationTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: false, emailEnabled: false));
        mockAuditLogger
            .Setup(x => x.LogAudit(It.IsAny<AuditEvent>()))
            .Callback<AuditEvent>(auditEvent => capturedAuditEvent = auditEvent);

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = notificationTypeId.ToString(), InAppEnabled = true, EmailEnabled = false }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        Assert.NotNull(capturedAuditEvent);
        Assert.Equal(profileId.ToString(), capturedAuditEvent.Properties["ProfileId"]);
        Assert.Equal("TargetUser", capturedAuditEvent.Properties["TargetUser"]);
        Assert.Equal($"{notificationTypeId}:InApp=True,Email=False", capturedAuditEvent.Properties["ChangedPreferences"]);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_UnknownNotificationType_IsRejectedWithoutMutation()
    {
        var profileId = Guid.NewGuid();
        var knownTypeId = Guid.NewGuid();
        var unknownTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(knownTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(knownTypeId, inSiteEnabled: false, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), knownTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = knownTypeId.ToString(), InAppEnabled = false, EmailEnabled = false },
                new() { NotificationTypeId = unknownTypeId.ToString(), InAppEnabled = true, EmailEnabled = true }
            ]
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(UserController.ManageProfile), view.ViewName);
        Assert.False(sut.ModelState.IsValid);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            It.IsAny<Guid>(),
            It.IsAny<List<EditNotificationPreferenceDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        AssertNoNotificationStateMutation();
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_UnsupportedChannel_IsRejectedWithoutMutation()
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId, supportsInSite: false));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: false, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = notificationTypeId.ToString(), InAppEnabled = true, EmailEnabled = false }
            ]
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(UserController.ManageProfile), view.ViewName);
        Assert.False(sut.ModelState.IsValid);
        mockRepositoryApiClient.Verify(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
            It.IsAny<Guid>(),
            It.IsAny<List<EditNotificationPreferenceDto>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        AssertNoNotificationStateMutation();
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_WhenUpdateFails_DoesNotReportSuccess()
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId);
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: false, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeId));
        SetupNotificationPreferenceUpdateFailure();

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = notificationTypeId.ToString(), InAppEnabled = true, EmailEnabled = true }
            ]
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(UserController.ManageProfile), view.ViewName);
        Assert.Contains("Failed to update notification preferences. Please try again.", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
        mockAuditLogger.Verify(x => x.LogAudit(It.IsAny<AuditEvent>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserNotificationPreferences_SuccessAlert_EncodesTargetDisplayName()
    {
        var profileId = Guid.NewGuid();
        var notificationTypeId = Guid.NewGuid();
        var sut = CreateSut(CreateGlobalAdminPrincipal(UserProfileClaimType.SeniorAdmin));

        SetupNotificationPreferencesAuthorizationSuccess();
        SetupUserProfile(profileId, displayName: "<img src=x onerror=alert(1)>");
        SetupGameServersList();
        SetupNotificationTypes(CreateNotificationType(notificationTypeId));
        SetupNotificationPreferences(CreateNotificationPreference(notificationTypeId, inSiteEnabled: false, emailEnabled: false));
        SetupNotificationHistory(CreateNotification(Guid.NewGuid(), notificationTypeId));

        var result = await sut.UpdateUserNotificationPreferences(new ManageUserNotificationPreferencesUpdateModel
        {
            Id = profileId,
            Preferences =
            [
                new() { NotificationTypeId = notificationTypeId.ToString(), InAppEnabled = true, EmailEnabled = true }
            ]
        });

        AssertRedirectsToManageProfileNotifications(result, profileId);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", sut.TempData["Alerts"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManageNotifications_Get_RedirectsToManageProfileNotificationsTab()
    {
        var profileId = Guid.NewGuid();
        var sut = CreateSut(CreateHeadAdminPrincipal(GameType.CallOfDuty5));

        var result = await sut.ManageNotifications(profileId);

        AssertRedirectsToManageProfileNotifications(result, profileId);
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

    private void SetupUsersLogOutAuthorizationFailure()
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.Users_LogOut))
            .ReturnsAsync(AuthorizationResult.Failed());
    }

    private void SetupNotificationPreferencesAuthorizationSuccess()
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.Users_ManageNotificationPreferences))
            .ReturnsAsync(AuthorizationResult.Success());
    }

    private void SetupNotificationPreferencesAuthorizationFailure()
    {
        mockAuthorizationService
            .Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), AuthPolicies.Users_ManageNotificationPreferences))
            .ReturnsAsync(AuthorizationResult.Failed());
    }

    private void SetupUserProfile(Guid profileId, IEnumerable<object>? claims = null, string forumId = "12345", string displayName = "Target User")
    {
        var userProfile = CreateUserProfileDto(profileId, forumId, claims ?? [], displayName);
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

    private void SetupGameServerNotFound(Guid serverId)
    {
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.NotFound));
    }

    private void SetupGameServersList(params GameServerDto[] gameServers)
    {
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]?>(),
                It.IsAny<Guid[]?>(),
                It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerOrder>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(gameServers))));
    }

    private void SetupGameServersListFailure()
    {
        mockRepositoryApiClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]?>(),
                It.IsAny<Guid[]?>(),
                It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerOrder>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.InternalServerError));
    }

    private void SetupNotificationTypes(params NotificationTypeDto[] notificationTypes)
    {
        mockRepositoryApiClient
            .Setup(x => x.NotificationTypes.V1.GetNotificationTypes(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationTypeDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationTypeDto>>(new CollectionModel<NotificationTypeDto>(notificationTypes))));
    }

    private void SetupNotificationTypesFailure()
    {
        mockRepositoryApiClient
            .Setup(x => x.NotificationTypes.V1.GetNotificationTypes(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationTypeDto>>(HttpStatusCode.InternalServerError));
    }

    private void SetupNotificationPreferences(params NotificationPreferenceDto[] notificationPreferences)
    {
        mockRepositoryApiClient
            .Setup(x => x.NotificationPreferences.V1.GetNotificationPreferences(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationPreferenceDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationPreferenceDto>>(new CollectionModel<NotificationPreferenceDto>(notificationPreferences))));
    }

    private void SetupNotificationHistory(params NotificationDto[] notifications)
    {
        mockRepositoryApiClient
            .Setup(x => x.Notifications.V1.GetNotifications(
                It.IsAny<Guid>(),
                It.IsAny<bool?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<NotificationOrder?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<NotificationDto>>(new CollectionModel<NotificationDto>(notifications))));
    }

    private void SetupNotificationPreferenceUpdateSuccess()
    {
        mockRepositoryApiClient
            .Setup(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
                It.IsAny<Guid>(),
                It.IsAny<List<EditNotificationPreferenceDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
    }

    private void SetupNotificationPreferenceUpdateFailure()
    {
        mockRepositoryApiClient
            .Setup(x => x.NotificationPreferences.V1.UpdateNotificationPreferences(
                It.IsAny<Guid>(),
                It.IsAny<List<EditNotificationPreferenceDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.InternalServerError));
    }

    private void SetupNotificationHistoryFailure()
    {
        mockRepositoryApiClient
            .Setup(x => x.Notifications.V1.GetNotifications(
                It.IsAny<Guid>(),
                It.IsAny<bool?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<NotificationOrder?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<NotificationDto>>(HttpStatusCode.InternalServerError));
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

    private void AssertNoNotificationStateMutation()
    {
        mockRepositoryApiClient.Verify(x => x.Notifications.V1.MarkNotificationAsRead(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        mockRepositoryApiClient.Verify(x => x.Notifications.V1.MarkAllNotificationsAsRead(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertRedirectsToManageProfileNotifications(IActionResult result, Guid profileId)
    {
        switch (result)
        {
            case RedirectToActionResult redirectToAction:
                Assert.Equal(nameof(UserController.ManageProfile), redirectToAction.ActionName);
                Assert.Equal(profileId, redirectToAction.RouteValues?["id"]);
                Assert.Equal(ManageUserProfileViewModel.NotificationsTabName, redirectToAction.RouteValues?["tab"]);
                break;
            case RedirectResult redirect:
                Assert.Equal($"/User/ManageProfile/{profileId}?tab=notifications#notifications", redirect.Url);
                break;
            default:
                throw new InvalidOperationException($"Expected a redirect result but got {result.GetType().Name}.");
        }
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

    private static bool HasNotificationPreference(
        IEnumerable<EditNotificationPreferenceDto> preferences,
        Guid notificationTypeId,
        bool inSiteEnabled,
        bool emailEnabled)
    {
        return preferences.Any(preference =>
            string.Equals(preference.NotificationTypeId, notificationTypeId.ToString(), StringComparison.OrdinalIgnoreCase) &&
            preference.InSiteEnabled == inSiteEnabled &&
            preference.EmailEnabled == emailEnabled);
    }

    private static UserProfileDto CreateUserProfileDto(Guid userProfileId, string forumId, IEnumerable<object> claims, string displayName = "Target User")
    {
        var json = JsonConvert.SerializeObject(new
        {
            UserProfileId = userProfileId,
            XtremeIdiotsForumId = forumId,
            DisplayName = displayName,
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

    private static NotificationTypeDto CreateNotificationType(
        Guid notificationTypeId,
        bool supportsEmail = true,
        bool supportsInSite = true,
        string? defaultChannels = null)
    {
        var json = JsonConvert.SerializeObject(new
        {
            NotificationTypeId = notificationTypeId.ToString(),
            DisplayName = $"Notification {notificationTypeId}",
            Description = "Route test notification",
            SupportsEmail = supportsEmail,
            SupportsInSite = supportsInSite,
            DefaultChannels = defaultChannels
        });

        return JsonConvert.DeserializeObject<NotificationTypeDto>(json)!;
    }

    private static NotificationPreferenceDto CreateNotificationPreference(Guid notificationTypeId, bool inSiteEnabled, bool emailEnabled)
    {
        var json = JsonConvert.SerializeObject(new
        {
            NotificationTypeId = notificationTypeId.ToString(),
            InSiteEnabled = inSiteEnabled,
            EmailEnabled = emailEnabled
        });

        return JsonConvert.DeserializeObject<NotificationPreferenceDto>(json)!;
    }

    private static NotificationDto CreateNotification(
        Guid notificationId,
        Guid notificationTypeId,
        string title = "Notification title",
        string message = "Notification message")
    {
        var json = JsonConvert.SerializeObject(new
        {
            NotificationId = notificationId,
            NotificationTypeId = notificationTypeId.ToString(),
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow,
            IsRead = false,
            EmailSent = true
        });

        return JsonConvert.DeserializeObject<NotificationDto>(json)!;
    }
}
