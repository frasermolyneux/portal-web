using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

/// <summary>
/// Fluent builder that assembles a <see cref="PlayerDto"/> (and its child collections) for the
/// Player Details Playwright coverage. Property names are matched case-insensitively by Newtonsoft
/// when the anonymous graph is serialized then deserialized into the immutable DTO, mirroring the
/// established <c>CredentialsContentScenario</c> / <c>AdminActionScenario</c> pattern.
/// </summary>
internal sealed class PlayerDtoBuilder
{
    private readonly List<object> aliases = [];
    private readonly List<object> ipAddresses = [];
    private readonly List<object> adminActions = [];
    private readonly List<object> relatedPlayers = [];
    private readonly List<object> protectedNames = [];
    private readonly List<object> tags = [];

    public Guid PlayerId { get; set; } = System.Guid.NewGuid();

    public GameType GameType { get; set; } = GameType.CallOfDuty4;

    public string Username { get; set; } = "DetailsPlayer";

    public string Guid { get; set; } = "DETAILS-GUID";

    public string SteamId { get; set; } = "76561198000000000";

    public string IpAddress { get; set; } = "203.0.113.10";

    public int? AliasCountOverride { get; set; }

    public int? IpAddressCountOverride { get; set; }

    public int? ProtectedNameCountOverride { get; set; }

    public int? TagCountOverride { get; set; }

    public int? RelatedPlayerCountOverride { get; set; }

    public PlayerDtoBuilder WithAlias(string name, int confidenceScore = 5)
    {
        aliases.Add(new
        {
            Name = name,
            Added = DateTime.UtcNow.AddDays(-10),
            LastUsed = DateTime.UtcNow,
            ConfidenceScore = confidenceScore,
        });
        return this;
    }

    public PlayerDtoBuilder WithIpAddress(string address, int confidenceScore = 5, DateTime? lastUsed = null)
    {
        ipAddresses.Add(new
        {
            Address = address,
            Added = DateTime.UtcNow.AddDays(-10),
            LastUsed = lastUsed ?? DateTime.UtcNow,
            ConfidenceScore = confidenceScore,
        });
        return this;
    }

    public PlayerDtoBuilder WithAdminAction(AdminActionType type, DateTime? expires = null, string adminDisplayName = "BanningAdmin", string? text = null)
    {
        adminActions.Add(new
        {
            AdminActionId = System.Guid.NewGuid(),
            PlayerId,
            Type = type.ToString(),
            Text = text ?? $"{type} reason",
            Created = DateTime.UtcNow.AddDays(-1),
            Expires = expires,
            UserProfile = new
            {
                DisplayName = adminDisplayName,
            },
        });
        return this;
    }

    public PlayerDtoBuilder WithProtectedName(string name, string createdBy = "OwnerAdmin")
    {
        protectedNames.Add(new
        {
            ProtectedNameId = System.Guid.NewGuid(),
            PlayerId,
            Name = name,
            CreatedOn = DateTime.UtcNow.AddDays(-3),
            CreatedByUserProfile = new
            {
                DisplayName = createdBy,
            },
        });
        return this;
    }

    public PlayerDtoBuilder WithRelatedPlayer(
        string username,
        string linkingIpAddress,
        bool hasActiveBan = false,
        int adminActionCount = 0,
        bool isCurrentIp = false,
        int sharedIpCount = 1,
        GameType? gameType = null)
    {
        relatedPlayers.Add(new
        {
            GameType = (gameType ?? GameType).ToString(),
            Username = username,
            PlayerId = System.Guid.NewGuid(),
            IpAddress = linkingIpAddress,
            LastSeen = DateTime.UtcNow.AddDays(-2),
            HasActiveBan = hasActiveBan,
            AdminActionCount = adminActionCount,
            LinkingIpAddress = linkingIpAddress,
            LinkingIpLastUsedByPlayer = DateTime.UtcNow.AddDays(-5),
            LinkingIpLastUsedByRelated = DateTime.UtcNow.AddDays(-4),
            IsCurrentIp = isCurrentIp,
            SharedIpCount = sharedIpCount,
        });
        return this;
    }

    public PlayerDtoBuilder WithTag(string name, string? tagHtml = null)
    {
        var tagId = System.Guid.NewGuid();
        tags.Add(new
        {
            PlayerTagId = System.Guid.NewGuid(),
            PlayerId,
            TagId = tagId,
            Tag = new
            {
                TagId = tagId,
                Name = name,
                TagHtml = tagHtml,
            },
        });
        return this;
    }

    public PlayerDto Build()
    {
        var json = JsonConvert.SerializeObject(new
        {
            PlayerId,
            GameType = GameType.ToString(),
            Username,
            Guid,
            SteamId,
            IpAddress,
            FirstSeen = DateTime.UtcNow.AddDays(-60),
            LastSeen = DateTime.UtcNow,
            PlayerAliases = aliases,
            PlayerIpAddresses = ipAddresses,
            AdminActions = adminActions,
            RelatedPlayers = relatedPlayers,
            ProtectedNames = protectedNames,
            Tags = tags,
            AliasCount = AliasCountOverride ?? aliases.Count,
            IpAddressCount = IpAddressCountOverride ?? ipAddresses.Count,
            AdminActionCount = adminActions.Count,
            RelatedPlayerCount = RelatedPlayerCountOverride ?? relatedPlayers.Count,
            ProtectedNameCount = ProtectedNameCountOverride ?? protectedNames.Count,
            TagCount = TagCountOverride ?? tags.Count,
        });

        return JsonConvert.DeserializeObject<PlayerDto>(json)!;
    }
}
