using Microsoft.EntityFrameworkCore;

namespace XtremeIdiots.Portal.Web.Areas.Identity.Data;

public sealed class IdentityDatabaseInitializer(IdentityDataContext identityDataContext) : IIdentityDatabaseInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return identityDataContext.Database.MigrateAsync(cancellationToken);
    }
}
