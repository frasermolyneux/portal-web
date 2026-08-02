using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class PlayersAuthHandlerTests
{
    [Fact]
    public async Task HandleAsync_Read_SucceedsForMatchingGameScopedRole()
    {
        var context = CreateContext(
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
            GameType.CallOfDuty4);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Read_DoesNotSucceedForDifferentGameScopedRole()
    {
        var context = CreateContext(
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
            GameType.CallOfDuty2);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Read_SucceedsForMatchingDirectPermission()
    {
        var context = CreateContext(
            new Claim(AuthPolicies.Players_Read, GameType.CallOfDuty2.ToString()),
            GameType.CallOfDuty2);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Read_DoesNotSucceedForDifferentGameDirectPermission()
    {
        var context = CreateContext(
            new Claim(AuthPolicies.Players_Read, GameType.CallOfDuty4.ToString()),
            GameType.CallOfDuty2);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Read_SucceedsForSeniorAdminAcrossGames()
    {
        var context = CreateContext(
            new Claim(UserProfileClaimType.SeniorAdmin, bool.TrueString),
            GameType.CallOfDuty2);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Read_SucceedsWithoutGameResourceForPolicyPrecheck()
    {
        var requirement = new PlayersRead();
        var user = CreateUser(new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await new PlayersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(Claim claim, GameType resource)
    {
        var requirement = new PlayersRead();
        return new AuthorizationHandlerContext([requirement], CreateUser(claim), resource);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuthType"));
    }
}
