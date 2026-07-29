namespace XtremeIdiots.Portal.Web.Areas.Identity.Data;

public interface IIdentityDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
