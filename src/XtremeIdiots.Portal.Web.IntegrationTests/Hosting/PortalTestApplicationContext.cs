using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MX.GeoLocation.Api.Client.V1;
using MX.InvisionCommunity.Api.Abstractions;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web;
using XtremeIdiots.Portal.Web.Areas.Identity.Data;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class PortalTestApplicationContext : IAsyncDisposable
{
    private readonly SqliteConnection sqliteConnection;
    private readonly ServiceProvider sqliteServiceProvider;
    private readonly TestDiagnosticScope diagnostics;
    private int disposed;

    private PortalTestApplicationContext(
        WebApplicationBuilder builder,
        SqliteConnection sqliteConnection,
        ServiceProvider sqliteServiceProvider,
        TestDiagnosticScope diagnostics)
    {
        Builder = builder;
        this.sqliteConnection = sqliteConnection;
        this.sqliteServiceProvider = sqliteServiceProvider;
        this.diagnostics = diagnostics;
    }

    public WebApplicationBuilder Builder { get; }

    public async static Task<PortalTestApplicationContext> CreateAsync(
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = TestDiagnosticScope.Current
            ?? throw new InvalidOperationException("Test application contexts require a host diagnostic scope.");
        var sqliteConnection = new SqliteConnection("Data Source=:memory:");
        ServiceProvider? sqliteServiceProvider = null;

        try
        {
            await sqliteConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
            sqliteServiceProvider = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();
            var builder = PortalWebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(PortalWebApplication).Assembly.FullName,
                    ContentRootPath = FindWebContentRoot(),
                    EnvironmentName = Environments.Development,
                },
                webApplicationBuilder => webApplicationBuilder.Configuration.AddInMemoryCollection(PortalTestConfiguration.Values));

            ReplaceIdentityDatabase(builder.Services, sqliteConnection, sqliteServiceProvider);
            builder.Logging.AddProvider(new DiagnosticLoggerProvider());
            builder.Services.AddTransient<IStartupFilter, DiagnosticRequestStartupFilter>();
            builder.Services.AddPortalTestAuthentication();
            ReplaceExternalApiClients(builder.Services);
            configureServices?.Invoke(builder.Services);
            cancellationToken.ThrowIfCancellationRequested();
            return new PortalTestApplicationContext(builder, sqliteConnection, sqliteServiceProvider, diagnostics);
        }
        catch (Exception exception)
        {
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (sqliteServiceProvider is not null)
            {
                await cleanup.RunAsync("Disposing failed SQLite services", () => sqliteServiceProvider.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            await cleanup.RunAsync("Closing failed SQLite connection", () => sqliteConnection.DisposeAsync().AsTask()).ConfigureAwait(false);
            cleanup.AttachToPrimaryException(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        await cleanup.RunAsync("Disposing SQLite services", () => sqliteServiceProvider.DisposeAsync().AsTask()).ConfigureAwait(false);
        await cleanup.RunAsync("Closing SQLite connection", () => sqliteConnection.DisposeAsync().AsTask()).ConfigureAwait(false);
        cleanup.ThrowIfFailed();
    }

    private static string FindWebContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XtremeIdiots.Portal.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("Could not locate the portal-web source directory.")
            : Path.Combine(directory.FullName, "XtremeIdiots.Portal.Web");
    }
    private static void ReplaceExternalApiClients(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IServersApiClient>();
        services.RemoveAll<IGeoLocationApiClient>();
        services.RemoveAll<IInvisionApiClient>();

        services.AddSingleton(new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        }.Object);
        services.AddSingleton(new Mock<IServersApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        }.Object);
        services.AddSingleton(new Mock<IGeoLocationApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        }.Object);
        services.AddSingleton(new Mock<IInvisionApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        }.Object);
    }

    private static void ReplaceIdentityDatabase(
        IServiceCollection services,
        SqliteConnection sqliteConnection,
        IServiceProvider sqliteServiceProvider)
    {
        services.RemoveAll<IDbContextOptionsConfiguration<IdentityDataContext>>();
        services.RemoveAll<IdentityDataContext>();
        services.RemoveAll<DbContextOptions<IdentityDataContext>>();
        services.RemoveAll<IIdentityDatabaseInitializer>();

        services.AddDbContext<IdentityDataContext>(options =>
            options.UseSqlite(sqliteConnection).UseInternalServiceProvider(sqliteServiceProvider));
        services.AddScoped<IIdentityDatabaseInitializer, TestIdentityDatabaseInitializer>();
    }
}
