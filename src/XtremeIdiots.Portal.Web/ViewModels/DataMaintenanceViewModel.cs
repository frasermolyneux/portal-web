using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class DataMaintenanceViewModel
{
    public string LookupPlayerId { get; set; } = string.Empty;

    public DataMaintenancePlayerPreviewViewModel? Player { get; set; }

    public string ConfirmationText { get; set; } = string.Empty;
}

public sealed class DataMaintenancePlayerPreviewViewModel
{
    public Guid PlayerId { get; set; }

    public GameType GameType { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PlayerGuid { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DateTime LastSeen { get; set; }

    public int AliasCount { get; set; }

    public int IpAddressCount { get; set; }

    public int AdminActionCount { get; set; }

    public int ProtectedNameCount { get; set; }

    public int RelatedPlayerCount { get; set; }

    public int TagCount { get; set; }
}
