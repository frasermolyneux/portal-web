using Microsoft.AspNetCore.Authorization;
using XtremeIdiots.Portal.Web.Auth.Handlers;
using XtremeIdiots.Portal.Web.Auth.XtremeIdiots;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddXtremeIdiotsAuth(this IServiceCollection services)
    {
        services.AddScoped<IXtremeIdiotsAuth, XtremeIdiotsAuth>();

        services.AddSingleton<IAuthorizationHandler, MapRotationsAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, MapsAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, GameServersAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, ChatLogAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, AdminActionsAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, PlayersAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, TagsAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, DashboardAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, DemosAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, GlobalSettingsAuthHandler>();
        services.AddSingleton<IAuthorizationHandler, UsersAuthHandler>();
    }
}