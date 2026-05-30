using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public class BannersControllerTests
{
    [Fact]
    public void BannersController_HasClassLevelAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(BannersController).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault();

        Assert.NotNull(authorizeAttribute);
    }

    [Fact]
    public void GetGameServers_HasAllowAnonymousAttribute_OnlyAnonymousActionInController()
    {
        var methods = typeof(BannersController).GetMethods();
        var allowAnonymousMethods = methods
            .Where(method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0)
            .ToList();

        Assert.Single(allowAnonymousMethods);
        Assert.Equal(nameof(BannersController.GetGameServers), allowAnonymousMethods[0].Name);
    }
}
