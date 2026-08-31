using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class BaseAuthorizationHelperEquivalenceTests
{
    [Theory]
    [InlineData(GameType.CallOfDuty4, GameType.CallOfDuty4x, true)]
    [InlineData(GameType.CallOfDuty4x, GameType.CallOfDuty4, true)]
    [InlineData(GameType.CallOfDuty4, GameType.CallOfDuty4, true)]
    [InlineData(GameType.CallOfDuty4x, GameType.CallOfDuty4x, true)]
    [InlineData(GameType.CallOfDuty4, GameType.CallOfDuty5, false)]
    [InlineData(GameType.CallOfDuty5, GameType.CallOfDuty4, false)]
    [InlineData(GameType.CallOfDuty5, GameType.CallOfDuty5, true)]
    public void AreGameTypesEquivalent_ReturnsExpected(GameType a, GameType b, bool expected)
    {
        Assert.Equal(expected, BaseAuthorizationHelper.AreGameTypesEquivalent(a, b));
    }

    [Theory]
    [InlineData(GameType.CallOfDuty4)]
    [InlineData(GameType.CallOfDuty4x)]
    public void GetEquivalentGameTypes_Cod4_ReturnsBothCod4AndCod4x(GameType gameType)
    {
        var equivalents = BaseAuthorizationHelper.GetEquivalentGameTypes(gameType);

        Assert.Contains(GameType.CallOfDuty4, equivalents);
        Assert.Contains(GameType.CallOfDuty4x, equivalents);
    }

    [Fact]
    public void GetEquivalentGameTypes_NonCod4_ReturnsSelf()
    {
        var equivalents = BaseAuthorizationHelper.GetEquivalentGameTypes(GameType.CallOfDuty5);

        Assert.Single(equivalents);
        Assert.Contains(GameType.CallOfDuty5, equivalents);
    }

    [Fact]
    public void CheckSeniorOrGameAdminAccessWithResource_NullResource_DoesNotSucceed()
    {
        var requirement = new PlayersRead();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString())],
            authenticationType: "TestAuthType"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        BaseAuthorizationHelper.CheckSeniorOrGameAdminAccessWithResource(context, requirement);

        Assert.False(context.HasSucceeded);
    }
}
