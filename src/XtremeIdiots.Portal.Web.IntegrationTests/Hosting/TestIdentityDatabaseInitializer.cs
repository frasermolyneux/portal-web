using XtremeIdiots.Portal.Web.Areas.Identity.Data;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class TestIdentityDatabaseInitializer(IdentityDataContext identityDataContext) : IIdentityDatabaseInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return identityDataContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
