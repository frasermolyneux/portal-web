using XtremeIdiots.Portal.Web;

var builder = PortalWebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
});
var app = PortalWebApplication.Build(builder);

await PortalWebApplication.InitializeAsync(app);
await app.RunAsync();

public partial class Program
{
}
