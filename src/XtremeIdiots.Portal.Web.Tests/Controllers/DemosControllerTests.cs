using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Demos;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class DemosControllerTests
{
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    [Fact]
    public void GetDemoListAjax_RequiresAntiforgeryValidation()
    {
        var method = typeof(DemosController).GetMethod(nameof(DemosController.GetDemoListAjax));
        Assert.NotNull(method);
        Assert.True(Attribute.IsDefined(method, typeof(ValidateAntiForgeryTokenAttribute)));
    }

    private DemosController CreateController()
    {
        var profile = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new
        {
            UserProfileId = Guid.NewGuid(),
            XtremeIdiotsForumId = "test-user"
        }))!;
        repository.Setup(x => x.UserProfiles.V1.GetUserProfileByDemoAuthKey(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<UserProfileDto>(HttpStatusCode.OK, new ApiResponse<UserProfileDto>(profile)));

        var userManager = new Mock<UserManager<IdentityUser>>(
            new Mock<IUserStore<IdentityUser>>().Object, null, null, null, null, null, null, null, null);
        var signInManager = new Mock<SignInManager<IdentityUser>>(
            userManager.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<IdentityUser>>().Object, null, null, null, null);
        var controller = new DemosController(
            new Mock<IAuthorizationService>().Object,
            userManager.Object,
            signInManager.Object,
            new Mock<IDemoManager>().Object,
            repository.Object,
            new TelemetryClient(new TelemetryConfiguration()),
            new Mock<ILogger<DemosController>>().Object,
            new Mock<IConfiguration>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IAuditLogger>().Object);
        var context = new DefaultHttpContext();
        context.Request.Headers["demo-manager-auth-key"] = "synthetic-test-key";
        context.Request.Headers["demo-manager-game-type"] = "CallOfDuty4";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    [Theory]
    [InlineData("../outside.dm_1")]
    [InlineData("..\\outside.dm_1")]
    [InlineData("/tmp/outside.dm_6")]
    [InlineData("C:outside.dm_1")]
    [InlineData("forged\nentry.dm_1")]
    [InlineData("outside.dm_1.txt")]
    public async Task ClientUploadDemo_RejectsUnsafeFileNames(string fileName)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("demo"));
        var file = new FormFile(content, 0, content.Length, "file", fileName);

        var result = Assert.IsType<ContentResult>(await CreateController().ClientUploadDemo(file));

        Assert.Equal("Invalid file type extension", result.Content);
        repository.Verify(x => x.Demos.V1.CreateDemo(It.IsAny<CreateDemoDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClientUploadDemo_RejectsUndefinedGameType()
    {
        var controller = CreateController();
        controller.Request.Headers["demo-manager-game-type"] = "2147483647";
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("demo"));
        var file = new FormFile(content, 0, content.Length, "file", "safe.dm_1");

        var result = Assert.IsType<ContentResult>(await controller.ClientUploadDemo(file));

        Assert.Contains("Invalid or missing game type", result.Content);
        repository.Verify(x => x.Demos.V1.CreateDemo(It.IsAny<CreateDemoDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClientUploadDemo_DeletesTemporaryFileWhenRepositoryFails()
    {
        string? temporaryPath = null;
        repository.Setup(x => x.Demos.V1.CreateDemo(It.IsAny<CreateDemoDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Synthetic failure"));
        var file = new Mock<IFormFile>();
        file.SetupGet(x => x.FileName).Returns("safe.dm_1");
        file.SetupGet(x => x.Length).Returns(4);
        file.Setup(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((destination, cancellationToken) =>
            {
                temporaryPath = Assert.IsType<FileStream>(destination).Name;
                return destination.WriteAsync(Encoding.UTF8.GetBytes("demo"), cancellationToken).AsTask();
            });

        var result = Assert.IsType<ContentResult>(await CreateController().ClientUploadDemo(file.Object));

        Assert.Contains("internal server error", result.Content);
        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.NotEqual(Path.Combine(Path.GetTempPath(), "safe.dm_1"), temporaryPath);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task ClientUploadDemo_UsesGeneratedTemporaryFileAndDeletesItAfterUpload(HttpStatusCode status, bool succeeds)
    {
        var demoId = Guid.NewGuid();
        var demo = JsonConvert.DeserializeObject<DemoDto>(JsonConvert.SerializeObject(new { DemoId = demoId }))!;
        repository.Setup(x => x.Demos.V1.CreateDemo(It.IsAny<CreateDemoDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<DemoDto>(HttpStatusCode.OK, new ApiResponse<DemoDto>(demo)));
        string? temporaryPath = null;
        repository.Setup(x => x.Demos.V1.SetDemoFile(demoId, "safe.DM_1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, CancellationToken>((_, _, path, _) =>
            {
                temporaryPath = path;
                Assert.Equal("demo", File.ReadAllText(path));
            })
            .ReturnsAsync(new ApiResult(status));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("demo"));
        var file = new FormFile(content, 0, content.Length, "file", "safe.DM_1");

        var result = await CreateController().ClientUploadDemo(file);

        if (succeeds)
            Assert.IsType<OkResult>(result);
        else
            Assert.Equal("Error: Failed to upload demo.", Assert.IsType<ContentResult>(result).Content);
        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.NotEqual(Path.Combine(Path.GetTempPath(), file.FileName), temporaryPath);
    }
}
