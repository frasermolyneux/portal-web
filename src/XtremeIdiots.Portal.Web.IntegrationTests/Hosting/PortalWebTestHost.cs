using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using XtremeIdiots.Portal.Web;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class PortalWebTestHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly PortalTestApplicationContext applicationContext;

    private PortalWebTestHost(WebApplication app, PortalTestApplicationContext applicationContext)
    {
        this.app = app;
        this.applicationContext = applicationContext;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => app.Services;

    public async static Task<PortalWebTestHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var applicationContext = await PortalTestApplicationContext.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        WebApplication? app = null;

        try
        {
            applicationContext.Builder.WebHost.UseTestServer();
            app = PortalWebApplication.Build(applicationContext.Builder);
            await PortalWebApplication.InitializeAsync(app, cancellationToken).ConfigureAwait(false);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            return new PortalWebTestHost(app, applicationContext);
        }
        catch
        {
            if (app is not null)
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }

            await applicationContext.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        await applicationContext.DisposeAsync().ConfigureAwait(false);
    }
}
