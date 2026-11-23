using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class GameServersAuthHandlerTests
{
    [Fact]
    public async Task HandleAsync_FileTransportRead_DoesNotSucceedForLegacyFtpClaim()
    {
        var serverId = Guid.NewGuid();
        var requirement = new GameServersCredentialsFileTransportRead();
        var user = CreateUser(new Claim("GameServers.Credentials.Ftp.Read", serverId.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4, serverId));

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_FileTransportRead_SucceedsForFileTransportClaim()
    {
        var serverId = Guid.NewGuid();
        var requirement = new GameServersCredentialsFileTransportRead();
        var user = CreateUser(new Claim(AuthPolicies.GameServers_Credentials_FileTransport_Read, serverId.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4, serverId));

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ScreenshotsRead_SucceedsForMatchingGameScopedClaim()
    {
        var requirement = new GameServersAdminScreenshotsRead();
        var user = CreateUser(new Claim(AuthPolicies.GameServers_Admin_Screenshots_Read, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ScreenshotsRead_DoesNotSucceedForDifferentGameScopedClaim()
    {
        var requirement = new GameServersAdminScreenshotsRead();
        var user = CreateUser(new Claim(AuthPolicies.GameServers_Admin_Screenshots_Read, GameType.Insurgency.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_RconScreenshot_SucceedsForMatchingGameScopedClaim()
    {
        var requirement = new GameServersAdminRconScreenshot();
        var user = CreateUser(new Claim(AuthPolicies.GameServers_Admin_Rcon_Screenshot, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ScreenshotsConfigure_SucceedsForMatchingGameScopedClaim()
    {
        var requirement = new GameServersAdminScreenshotsConfigure();
        var user = CreateUser(new Claim(AuthPolicies.GameServers_Admin_Screenshots_Configure, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_CoD4xPluginLifecycle_SucceedsForHeadAdmin()
    {
        var requirement = new GameServersAdminCoD4xPluginLifecycle();
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4x.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4x);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_CoD4xPluginLifecycle_SucceedsForDirectPermissionGrant()
    {
        var requirement = new GameServersAdminCoD4xPluginLifecycle();
        var user = CreateUser(new Claim(AdditionalPermission.GameServers_Admin_CoD4xPluginLifecycle, GameType.CallOfDuty4x.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, GameType.CallOfDuty4x);

        var sut = new GameServersAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    public static TheoryData<string, string, bool> SiegeModeClaims => new()
    {
        { UserProfileClaimType.SeniorAdmin, "true", true },
        { UserProfileClaimType.HeadAdmin, "CallOfDuty4", true },
        { UserProfileClaimType.HeadAdmin, "Insurgency", false },
        { UserProfileClaimType.GameAdmin, "CallOfDuty4", false },
        { AuthPolicies.GameServers_Write, "CallOfDuty4", false }
    };

    [Theory]
    [MemberData(nameof(SiegeModeClaims))]
    public async Task HandleAsync_SiegeMode_RequiresSeniorOrMatchingHeadAdmin(string claimType, string value, bool allowed)
    {
        var requirement = new GameServersAdminSiegeMode();
        var context = new AuthorizationHandlerContext([requirement], CreateUser(new Claim(claimType, value)), GameType.CallOfDuty4);

        await new GameServersAuthHandler().HandleAsync(context);

        Assert.Equal(allowed, context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_SiegeMode_DeniesMissingResourceEvenForSenior()
    {
        var requirement = new GameServersAdminSiegeMode();
        var context = new AuthorizationHandlerContext([requirement], CreateUser(new Claim(UserProfileClaimType.SeniorAdmin, "true")), null);

        await new GameServersAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_SiegeMode_AllowsPotentialAccessForValidHeadAdmin()
    {
        var requirement = new GameServersAdminSiegeMode();
        var context = new AuthorizationHandlerContext([requirement],
            CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString())), PotentialAccessProbe.Instance);

        await new GameServersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_SiegeMode_RejectsInvalidGameTypeAndPotentialClaim()
    {
        var requirement = new GameServersAdminSiegeMode();
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, "99999"));
        var invalidGame = new AuthorizationHandlerContext([requirement], user, (GameType)99999);
        var potential = new AuthorizationHandlerContext([requirement], user, PotentialAccessProbe.Instance);
        var sut = new GameServersAuthHandler();

        await sut.HandleAsync(invalidGame);
        await sut.HandleAsync(potential);

        Assert.False(invalidGame.HasSucceeded);
        Assert.False(potential.HasSucceeded);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new ClaimsPrincipal(identity);
    }
}
