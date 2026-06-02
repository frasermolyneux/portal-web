using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
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

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new ClaimsPrincipal(identity);
    }
}
