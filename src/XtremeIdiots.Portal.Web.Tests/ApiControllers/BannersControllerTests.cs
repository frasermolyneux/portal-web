using Microsoft.AspNetCore.Authorization;
using System.Reflection;
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
    public void BannerListActions_AreTheOnlyAnonymousActionsInController()
    {
        var methods = typeof(BannersController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var allowAnonymousMethods = methods
            .Where(method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0)
            .ToList();

        Assert.Equal(
            [nameof(BannersController.GetGameServers), nameof(BannersController.GetIntegrationBanners)],
            allowAnonymousMethods.Select(method => method.Name).Order().ToArray());
    }
}
