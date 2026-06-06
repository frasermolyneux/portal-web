using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.Requirements;

namespace XtremeIdiots.Portal.Web.Tests.Auth.Handlers;

public class AdminActionsAuthHandlerTests
{
    [Fact]
    public async Task HandleAsync_Create_SucceedsForCod4ClaimOnCod4xResource()
    {
        var requirement = new AdminActionsCreate();
        var user = CreateUser(new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4x, AdminActionType.Ban));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Create_SucceedsForCod4PermissionClaimOnCod4xResource()
    {
        var requirement = new AdminActionsCreate();
        var user = CreateUser(new Claim(AuthPolicies.AdminActions_Create, GameType.CallOfDuty4.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4x, AdminActionType.Ban));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Create_SucceedsForCod4xClaimOnCod4Resource()
    {
        var requirement = new AdminActionsCreate();
        var user = CreateUser(new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4x.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4, AdminActionType.Ban));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Create_DoesNotSucceedForDifferentGame()
    {
        var requirement = new AdminActionsCreate();
        var user = CreateUser(new Claim(UserProfileClaimType.GameAdmin, GameType.Insurgency.ToString()));
        var context = new AuthorizationHandlerContext([requirement], user, (GameType.CallOfDuty4x, AdminActionType.Ban));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_EditObservation_SucceedsForCod4ModeratorOnCod4xWhenOwner()
    {
        var requirement = new AdminActionsEdit();
        var ownerId = "owner-1";
        var user = CreateUser(
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
            new Claim(UserProfileClaimType.XtremeIdiotsId, ownerId));

        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            (GameType.CallOfDuty4x, AdminActionType.Observation, ownerId));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_EditObservation_DoesNotSucceedForCod4ModeratorOnCod4xWhenNotOwner()
    {
        var requirement = new AdminActionsEdit();
        var ownerId = "owner-1";
        var user = CreateUser(
            new Claim(UserProfileClaimType.Moderator, GameType.CallOfDuty4.ToString()),
            new Claim(UserProfileClaimType.XtremeIdiotsId, "different-owner"));

        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            (GameType.CallOfDuty4x, AdminActionType.Observation, ownerId));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Lift_SucceedsForCod4GameAdminOnCod4xWhenOwner()
    {
        var requirement = new AdminActionsLift();
        var ownerId = "owner-2";
        var user = CreateUser(
            new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()),
            new Claim(UserProfileClaimType.XtremeIdiotsId, ownerId));

        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            (GameType.CallOfDuty4x, ownerId));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_Lift_DoesNotSucceedForCod4GameAdminOnCod4xWhenNotOwner()
    {
        var requirement = new AdminActionsLift();
        var ownerId = "owner-2";
        var user = CreateUser(
            new Claim(UserProfileClaimType.GameAdmin, GameType.CallOfDuty4.ToString()),
            new Claim(UserProfileClaimType.XtremeIdiotsId, "different-owner"));

        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            new Tuple<GameType, string>(GameType.CallOfDuty4x, ownerId));

        var sut = new AdminActionsAuthHandler();
        await sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuthType");
        return new ClaimsPrincipal(identity);
    }
}
