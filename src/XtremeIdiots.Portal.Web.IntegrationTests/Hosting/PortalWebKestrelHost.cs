using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using XtremeIdiots.Portal.Web;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class PortalWebKestrelHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly PortalTestApplicationContext applicationContext;

    private PortalWebKestrelHost(WebApplication app, PortalTestApplicationContext applicationContext, Uri baseAddress)
    {
        this.app = app;
        this.applicationContext = applicationContext;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public async static Task<PortalWebKestrelHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var applicationContext = await PortalTestApplicationContext.CreateAsync(cancellationToken).ConfigureAwait(false);
        WebApplication? app = null;

        try
        {
            applicationContext.Builder.WebHost.UseUrls("http://127.0.0.1:0");
            app = PortalWebApplication.Build(applicationContext.Builder);
            await PortalWebApplication.InitializeAsync(app, cancellationToken).ConfigureAwait(false);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = addresses?.SingleOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Kestrel did not expose a loopback HTTP address.");

            return new PortalWebKestrelHost(app, applicationContext, new Uri(address));
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
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        await applicationContext.DisposeAsync().ConfigureAwait(false);
    }
}
