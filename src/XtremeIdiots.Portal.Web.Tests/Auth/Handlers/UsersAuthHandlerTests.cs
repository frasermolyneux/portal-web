using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class UsersAuthHandlerTests
{
#pragma warning disable IDE0028
    public static TheoryData<string> LowerAdminClaimTypes => new()
    {
        UserProfileClaimType.GameAdmin,
        UserProfileClaimType.Moderator
    };

    public static TheoryData<string> GlobalAdminClaimTypes => new()
    {
        UserProfileClaimType.SeniorAdmin,
        UserProfileClaimType.Webmaster
    };
#pragma warning restore IDE0028

    [Fact]
    public async Task HandleAsync_UsersRead_SucceedsForCod5HeadAdmin()
    {
        var context = CreateContext(new UsersRead(), CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString())));

        await new UsersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(GameType.CallOfDuty5, true)]
    [InlineData(GameType.CallOfDuty4, false)]
    public async Task HandleAsync_UsersManageClaims_UsesResolvedGameTypeScope(GameType resourceGameType, bool expected)
    {
        var context = CreateContext(
            new UsersManageClaims(),
            CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString())),
            resourceGameType);

        await new UsersAuthHandler().HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_UsersManageClaims_PotentialAccessProbe_SucceedsForCod5HeadAdmin()
    {
        var context = CreateContext(
            new UsersManageClaims(),
            CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString())),
            PotentialAccessProbe.Instance);

        await new UsersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(LowerAdminClaimTypes))]
    public async Task HandleAsync_UsersReadAndManageClaims_DenyLowerAdminLevels(string claimType)
    {
        var user = CreateUser(new Claim(claimType, GameType.CallOfDuty5.ToString()));

        var readContext = CreateContext(new UsersRead(), user);
        await new UsersAuthHandler().HandleAsync(readContext);

        var manageContext = CreateContext(new UsersManageClaims(), user, GameType.CallOfDuty5);
        await new UsersAuthHandler().HandleAsync(manageContext);

        Assert.False(readContext.HasSucceeded);
        Assert.False(manageContext.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(GlobalAdminClaimTypes))]
    public async Task HandleAsync_GlobalUserPolicies_SucceedForSeniorAdminAndWebmaster(string claimType)
    {
        var user = CreateUser(new Claim(claimType, bool.TrueString));

        var readContext = CreateContext(new UsersRead(), user);
        await new UsersAuthHandler().HandleAsync(readContext);

        var manageClaimsContext = CreateContext(new UsersManageClaims(), user, GameType.CallOfDuty4);
        await new UsersAuthHandler().HandleAsync(manageClaimsContext);

        var logoutContext = CreateContext(new UsersLogOut(), user);
        await new UsersAuthHandler().HandleAsync(logoutContext);

        var notificationsContext = CreateContext(new UsersManageNotificationPreferences(), user);
        await new UsersAuthHandler().HandleAsync(notificationsContext);

        Assert.True(readContext.HasSucceeded);
        Assert.True(manageClaimsContext.HasSucceeded);
        Assert.True(logoutContext.HasSucceeded);
        Assert.True(notificationsContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_UsersLogOut_SucceedsForHeadAdmin()
    {
        var context = CreateContext(new UsersLogOut(), CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString())));

        await new UsersAuthHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_UsersManageNotificationPreferences_DeniesHeadAdmin()
    {
        var context = CreateContext(
            new UsersManageNotificationPreferences(),
            CreateUser(new Claim(UserProfileClaimType.HeadAdmin, GameType.CallOfDuty5.ToString())));

        await new UsersAuthHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(
        IAuthorizationRequirement requirement,
        ClaimsPrincipal user,
        object? resource = null)
    {
        return new AuthorizationHandlerContext([requirement], user, resource);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }
}
