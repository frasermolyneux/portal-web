using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class MapRotationsAuthHandlerTests
{
    private readonly static Guid allowedServerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly static Guid deniedServerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Deploy_ServerScopedGrant_MatchingServer_Succeeds()
    {
        var user = CreateUser(new Claim(AuthPolicies.MapRotations_Deploy, allowedServerId.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_ServerScopedGrant_DifferentServer_Fails()
    {
        var user = CreateUser(new Claim(AuthPolicies.MapRotations_Deploy, allowedServerId.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, deniedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_BareGameType_DoesNotSatisfyServerGuidGrant()
    {
        var user = CreateUser(new Claim(AuthPolicies.MapRotations_Deploy, allowedServerId.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, GameType.CallOfDuty4);

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_GameScopedGrant_MatchesTuple()
    {
        var user = CreateUser(new Claim(AuthPolicies.MapRotations_Deploy, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_Cod4HeadAdmin_SucceedsForCod4Tuple()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_Cod4GameAdmin_SucceedsForCod4Tuple()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_Cod5HeadAdmin_FailsForCod4Tuple()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_SeniorAdmin_SucceedsForAnyTuple()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.SeniorAdmin, "true"));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_Cod4xEquivalence_Cod4HeadAdminSucceedsForCod4xTuple()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, (GameType.CallOfDuty4x, allowedServerId));

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_NullResource_FailsClosed()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, null);

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Deploy_PotentialAccessProbe_SucceedsForGameAdmin()
    {
        var user = CreateUser(new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext(
            [new MapRotationsDeploy()], user, PotentialAccessProbe.Instance);

        await new MapRotationsAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new ClaimsPrincipal(identity);
    }
}
