using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed record SiegeModeBannerItem(Guid ServerId, string ServerName, SiegeSessionDto Session);
