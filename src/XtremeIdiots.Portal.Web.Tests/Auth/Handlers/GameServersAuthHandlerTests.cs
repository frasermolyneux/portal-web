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

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new ClaimsPrincipal(identity);
    }
}
