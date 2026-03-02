using System.Reflection;

namespace XtremeIdiots.Portal.Web;

public static class InfoEndpointExtensions
{
    public static IEndpointRouteBuilder MapInfoEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/info", () =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "unknown";
            var assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";

            return Results.Ok(new
            {
                Version = informationalVersion,
                BuildVersion = informationalVersion.Split('+')[0],
                AssemblyVersion = assemblyVersion
            });
        }).AllowAnonymous();

        return endpoints;
    }
}
