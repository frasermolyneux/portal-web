using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Areas.Identity;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class ForumIdentityControllerTests
{
    private readonly Mock<UserManager<IdentityUser>> users = new(
        Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
    private readonly Mock<SignInManager<IdentityUser>> signIn;
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly IdentityUser user = new() { Id = "42" };

    public ForumIdentityControllerTests()
    {
        signIn = new Mock<SignInManager<IdentityUser>>(users.Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(), Options.Create(new IdentityOptions()),
            Mock.Of<ILogger<SignInManager<IdentityUser>>>(), Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<IdentityUser>>());
        users.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
    }

    [Fact]
    public async Task ProductionConfirmation_RejectsDifferentSubject()
    {
        signIn.Setup(manager => manager.GetExternalLoginInfoAsync(user.Id))
            .ReturnsAsync(Login("XtremeIdiots", "43"));

        var result = await CreateController().ConfirmProduction(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task TestConfirmation_RejectsLoginLinkedToAnotherUser()
    {
        SetupActiveProfile();
        var login = Login(IdentityHostingStartup.TestOAuthSchemeName, "test-subject");
        signIn.Setup(manager => manager.GetExternalLoginInfoAsync(user.Id)).ReturnsAsync(login);
        users.Setup(manager => manager.FindByLoginAsync(login.LoginProvider, login.ProviderKey))
            .ReturnsAsync(new IdentityUser { Id = "other" });

        var result = await CreateController().ConfirmTest(CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task TestConfirmation_RejectsMissingXsrfBoundExternalInfo()
    {
        signIn.Setup(manager => manager.GetExternalLoginInfoAsync(user.Id)).ReturnsAsync((ExternalLoginInfo?)null);

        var result = await CreateController().ConfirmTest(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task TestConfirmation_BindsVerifiedSubjectToExistingProductionUser()
    {
        SetupActiveProfile();
        var login = Login(IdentityHostingStartup.TestOAuthSchemeName, "scoped-subject");
        signIn.Setup(manager => manager.GetExternalLoginInfoAsync(user.Id)).ReturnsAsync(login);
        users.Setup(manager => manager.FindByLoginAsync(login.LoginProvider, login.ProviderKey)).ReturnsAsync((IdentityUser?)null);
        users.Setup(manager => manager.AddLoginAsync(user, login)).ReturnsAsync(IdentityResult.Success);

        var result = await CreateController().ConfirmTest(CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        users.Verify(manager => manager.AddLoginAsync(user, login), Times.Once);
        users.Verify(manager => manager.CreateAsync(It.IsAny<IdentityUser>()), Times.Never);
    }

    private ForumIdentityController CreateController()
        => new(signIn.Object, users.Object, Mock.Of<IAuthenticationSchemeProvider>(), repository.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private void SetupActiveProfile()
    {
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new { UserProfileId = Guid.NewGuid() }))!;
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));
    }

    private static ExternalLoginInfo Login(string provider, string subject)
        => new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], "Test")), provider, subject, provider);
}
