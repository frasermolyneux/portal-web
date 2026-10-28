using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MX.Api.Abstractions;
using System.Net;
using System.Security.Claims;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Controllers;
using XtremeIdiots.Redirect.Api.Abstractions.V1;
using XtremeIdiots.Redirect.Api.Client.Testing;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public sealed class MapDownloadsControllerTests
{
    private static MapDownloadsController Controller(FakeRedirectApiClient fake, string? member = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.12");
        context.Request.Headers.UserAgent = new string('a', 700);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.200";
        if (member is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(UserProfileClaimType.XtremeIdiotsId, member), new Claim(ClaimTypes.NameIdentifier, "999")], "Portal"));
        return new MapDownloadsController(fake) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Fact]
    public async Task Caller_UsesVerifiedForumClaimAndConnection_NotBrowserHeaders()
    {
        var fake = new FakeRedirectApiClient().WithData("Browse", RedirectDtoFactory.CreateCatalogue());
        var controller = Controller(fake, "123");
        Assert.IsType<JsonResult>(await controller.Browse("redirect/cod4"));
        var caller = Assert.Single(fake.PortalCalls);
        Assert.Equal("123", caller.MemberId);
        Assert.Equal("192.0.2.12", caller.IpAddress);
        Assert.Equal(512, caller.UserAgent.Length);
    }

    [Fact]
    public async Task AnonymousCaller_DoesNotInheritUnverifiedClaims()
    {
        var fake = new FakeRedirectApiClient().WithData("Browse", RedirectDtoFactory.CreateCatalogue());
        var controller = Controller(fake);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(UserProfileClaimType.XtremeIdiotsId, "456")]));
        await controller.Browse("redirect/cod4");
        Assert.Null(Assert.Single(fake.PortalCalls).MemberId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-forum-id")]
    [InlineData("0")]
    public async Task InvalidAuthenticatedForumIdentity_FailsClosed(string member)
    {
        var fake = new FakeRedirectApiClient();
        var result = Assert.IsType<StatusCodeResult>(await Controller(fake, member).Browse("redirect/cod4"));
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(fake.PortalCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ArchiveSelectionOutsideBound_DoesNotReachApi(int count)
    {
        var fake = new FakeRedirectApiClient();
        var paths = Enumerable.Range(0, count).Select(value => $"redirect/cod4/usermaps/mp_{value}").ToArray();
        Assert.IsType<BadRequestObjectResult>(await Controller(fake, "123").CreateArchive(new CreateArchive(paths)));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task HundredFolders_ReachDelegatedApi_WithoutExposingLegacyArchiveUrl()
    {
        var job = RedirectDtoFactory.CreateArchive(status: "complete");
        var fake = new FakeRedirectApiClient().WithData("CreateArchive", job);
        var paths = Enumerable.Range(0, 100).Select(value => $"redirect/cod4/usermaps/mp_{value}").ToArray();
        var result = Assert.IsType<JsonResult>(await Controller(fake, "123").CreateArchive(new CreateArchive(paths)));
        Assert.Null(Assert.IsType<ArchiveJob>(result.Value).DownloadUrl);
        Assert.Equal("123", Assert.Single(fake.PortalCalls).MemberId);
    }

    [Fact]
    public async Task CrossUserForbidden_IsPreservedAndNeverReplacedByAppIdentity()
    {
        var fake = new FakeRedirectApiClient().Respond("ArchiveStatus", new ApiResult<ArchiveJob>(
            HttpStatusCode.Forbidden, new ApiResponse<ArchiveJob>(new ApiError("FORBIDDEN", "private server detail"))));
        var result = Assert.IsType<ObjectResult>(await Controller(fake, "456").ArchiveStatus("another-users-job"));
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("456", Assert.Single(fake.PortalCalls).MemberId);
        Assert.DoesNotContain("private server detail", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task FileGrant_RequiresExactVersion_AndArchiveGrantRequiresSession()
    {
        var fake = new FakeRedirectApiClient();
        var controller = Controller(fake);
        Assert.IsType<BadRequestObjectResult>(await controller.Grant(new CreateDownloadGrant(Path: "redirect/cod4/file.ff")));
        Assert.IsType<UnauthorizedResult>(await controller.Grant(new CreateDownloadGrant(ArchiveId: "job")));
        Assert.Empty(fake.Calls);
    }
}
