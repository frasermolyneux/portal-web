using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MX.Api.Abstractions;
using MX.InvisionCommunity.Api.Abstractions;
using MX.InvisionCommunity.Api.Abstractions.Models;
using Newtonsoft.Json;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.XtremeIdiots;

namespace XtremeIdiots.Portal.Web.Tests.Auth;

public class XtremeIdiotsAuthTests
{
    private readonly Mock<UserManager<IdentityUser>> users = new(
        Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
    private readonly Mock<SignInManager<IdentityUser>> signIn;
    private readonly Mock<IInvisionApiClient> forums = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    public XtremeIdiotsAuthTests()
    {
        signIn = new Mock<SignInManager<IdentityUser>>(
            users.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<ILogger<SignInManager<IdentityUser>>>(),
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<IdentityUser>>());
        signIn.Setup(manager => manager.SignOutAsync()).Returns(Task.CompletedTask);
        var member = JsonConvert.DeserializeObject<MemberDto>(JsonConvert.SerializeObject(new
        {
            Id = 42,
            Name = "Former User",
            Email = "former@example.invalid"
        }));
        forums.Setup(client => client.Core.GetMember("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MemberDto>(HttpStatusCode.OK, new ApiResponse<MemberDto>(member)));
    }

    [Fact]
    public async Task ArchivedIdentityConflict_CannotCreateLocalUserOrIssueCookie()
    {
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));
        repository.Setup(client => client.UserProfiles.V1.CreateUserProfile(
            It.IsAny<CreateUserProfileDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult(HttpStatusCode.Conflict));

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        AssertNoSignIn();
        users.Verify(manager => manager.CreateAsync(It.IsAny<IdentityUser>()), Times.Never);
        signIn.Verify(manager => manager.SignOutAsync(), Times.Once);
    }

    [Fact]
    public async Task ArchivedProfilePayload_CannotIssueCookieOrCopyClaims()
    {
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = Guid.NewGuid(),
            XtremeIdiotsForumId = "42",
            ArchivedAtUtc = DateTime.UtcNow,
            UserProfileClaims = Array.Empty<object>()
        }));
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        AssertNoSignIn();
        users.Verify(manager => manager.AddClaimsAsync(It.IsAny<IdentityUser>(), It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }

    [Fact]
    public async Task ProfileLookupFailure_DoesNotCreateProfileOrSignIn()
    {
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.ServiceUnavailable));

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        AssertNoSignIn();
        repository.Verify(client => client.UserProfiles.V1.CreateUserProfile(
            It.IsAny<CreateUserProfileDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailedLocalUserCreation_DoesNotReportSuccessfulLogin()
    {
        SetupActiveProfile();
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true))
            .ReturnsAsync(SignInResult.Failed);
        users.Setup(manager => manager.CreateAsync(It.IsAny<IdentityUser>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "StoreUnavailable" }));

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        signIn.Verify(manager => manager.SignOutAsync(), Times.Once);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
    }

    [Fact]
    public async Task FailedClaimRefresh_ClearsTheExternalSignInCookie()
    {
        SetupActiveProfile();
        var user = new IdentityUser { Id = "42" };
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true))
            .ReturnsAsync(SignInResult.Success);
        users.Setup(manager => manager.FindByLoginAsync("XtremeIdiots", "42")).ReturnsAsync(user);
        users.Setup(manager => manager.GetClaimsAsync(user)).ReturnsAsync([]);
        users.Setup(manager => manager.RemoveClaimsAsync(user, It.IsAny<IEnumerable<Claim>>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "StoreUnavailable" }));

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        signIn.Verify(manager => manager.SignOutAsync(), Times.Once);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        users.Verify(manager => manager.AddClaimsAsync(user, It.IsAny<IEnumerable<Claim>>()), Times.Never);
    }

    [Fact]
    public async Task ArchivalDuringLocalUserCreation_CannotIssueCookie()
    {
        SetupActiveProfile();
        var creationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCreation = new TaskCompletionSource<IdentityResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true))
            .ReturnsAsync(SignInResult.Failed);
        users.Setup(manager => manager.CreateAsync(It.IsAny<IdentityUser>())).Returns(() =>
        {
            creationStarted.SetResult();
            return finishCreation.Task;
        });

        var login = CreateSut().ProcessExternalLogin(CreateLogin());
        await creationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));
        finishCreation.SetResult(IdentityResult.Success);

        var result = await login;

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        signIn.Verify(manager => manager.SignOutAsync(), Times.Once);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        repository.Verify(client => client.UserProfiles.V1.CreateUserProfile(
            It.IsAny<CreateUserProfileDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchivalAfterExternalSignIn_ClearsCookieBeforeRefreshingClaims()
    {
        var profile = SetupActiveProfile();
        repository.SetupSequence(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.NotFound));
        var user = new IdentityUser { Id = "42" };
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true))
            .ReturnsAsync(SignInResult.Success);
        users.Setup(manager => manager.FindByLoginAsync("XtremeIdiots", "42")).ReturnsAsync(user);

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        signIn.Verify(manager => manager.SignOutAsync(), Times.Once);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        users.Verify(manager => manager.RemoveClaimsAsync(user, It.IsAny<IEnumerable<Claim>>()), Times.Never);
        repository.Verify(client => client.UserProfiles.V1.CreateUserProfile(
            It.IsAny<CreateUserProfileDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedProfileRecheck_CanResumeUnlinkedRegistrationAfterRecovery(bool recheckThrows)
    {
        var profile = SetupActiveProfile();
        var response = new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile));
        var reads = repository.SetupSequence(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()));
        reads.ReturnsAsync(response);
        if (recheckThrows)
            reads.ThrowsAsync(new HttpRequestException("Repository unavailable."));
        else
            reads.ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.ServiceUnavailable));
        reads.ReturnsAsync(response).ReturnsAsync(response);

        IdentityUser? persistedUser = null;
        users.Setup(manager => manager.FindByIdAsync("42")).ReturnsAsync(() => persistedUser);
        users.Setup(manager => manager.CreateAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) =>
        {
            if (persistedUser is not null)
                return IdentityResult.Failed(new IdentityError { Code = "DuplicateUserId" });
            persistedUser = user;
            return IdentityResult.Success;
        });
        users.Setup(manager => manager.GetLoginsAsync(It.IsAny<IdentityUser>())).ReturnsAsync([]);
        users.Setup(manager => manager.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
        users.Setup(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(manager => manager.AddClaimsAsync(It.IsAny<IdentityUser>(), It.IsAny<IEnumerable<Claim>>())).ReturnsAsync(IdentityResult.Success);
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true)).ReturnsAsync(SignInResult.Failed);
        signIn.Setup(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), true, It.IsAny<string>())).Returns(Task.CompletedTask);
        signIn.Setup(manager => manager.RefreshSignInAsync(It.IsAny<IdentityUser>())).Returns(Task.CompletedTask);
        var sut = CreateSut();

        var firstAttempt = await sut.ProcessExternalLogin(CreateLogin());
        var retry = await sut.ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, firstAttempt);
        Assert.Equal(XtremeIdiotsAuthResult.Success, retry);
        users.Verify(manager => manager.CreateAsync(It.IsAny<IdentityUser>()), Times.Once);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Once);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), true, It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("login")]
    [InlineData("lockout")]
    public async Task IncompleteRegistration_DoesNotReuseProtectedLocalAccounts(string protection)
    {
        SetupActiveProfile();
        var user = new IdentityUser
        {
            Id = "42",
            PasswordHash = protection == "password" ? "synthetic-password-hash" : null
        };
        users.Setup(manager => manager.FindByIdAsync("42")).ReturnsAsync(user);
        users.Setup(manager => manager.GetLoginsAsync(user)).ReturnsAsync(
            protection == "login" ? [new UserLoginInfo("OtherProvider", "other-key", "Other provider")] : []);
        users.Setup(manager => manager.IsLockedOutAsync(user)).ReturnsAsync(protection == "lockout");
        signIn.Setup(manager => manager.ExternalLoginSignInAsync("XtremeIdiots", "42", true)).ReturnsAsync(SignInResult.Failed);

        var result = await CreateSut().ProcessExternalLogin(CreateLogin());

        Assert.Equal(XtremeIdiotsAuthResult.Failed, result);
        users.Verify(manager => manager.CreateAsync(It.IsAny<IdentityUser>()), Times.Never);
        users.Verify(manager => manager.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>()), Times.Never);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    private UserProfileDto SetupActiveProfile()
    {
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = Guid.NewGuid(),
            XtremeIdiotsForumId = "42",
            UserProfileClaims = Array.Empty<object>()
        })) ?? throw new InvalidOperationException("Unable to create the active profile fixture.");
        repository.Setup(client => client.UserProfiles.V1.GetUserProfileByXtremeIdiotsId("42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));
        return profile;
    }

    private XtremeIdiotsAuth CreateSut()
    {
        return new(
        Mock.Of<ILogger<XtremeIdiotsAuth>>(), signIn.Object, users.Object, forums.Object, repository.Object);
    }

    private void AssertNoSignIn()
    {
        signIn.Verify(manager => manager.ExternalLoginSignInAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        signIn.Verify(manager => manager.SignInAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    private static ExternalLoginInfo CreateLogin()
    {
        return new(
        new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "Former User"),
            new Claim(ClaimTypes.Email, "former@example.invalid")
        ], "Test")),
        "XtremeIdiots", "42", "XtremeIdiots");
    }
}
