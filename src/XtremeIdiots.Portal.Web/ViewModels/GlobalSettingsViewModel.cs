using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;

namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// View model for the global settings admin page, representing fleet-wide configuration defaults
/// </summary>
public class GlobalSettingsViewModel : IValidatableObject
{
    public const int DisabledSeverityThreshold = -1;
    public const string DefaultAgentName = "^4[^1>XI< BOT^4]^7";
    public const int MaxFunnyMessageLength = 120;

    // Agent defaults
    [DisplayName("Poll Interval (ms)")]
    [Range(100, int.MaxValue, ErrorMessage = "Poll interval must be at least 100ms.")]
    public int AgentPollIntervalMs { get; set; } = 500;

    [DisplayName("Status Publish Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Status publish interval must be at least 1 second.")]
    public int AgentStatusPublishIntervalSeconds { get; set; } = 60;

    [DisplayName("RCON Sync Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "RCON sync interval must be at least 1 second.")]
    public int AgentRconSyncIntervalSeconds { get; set; } = 300;

    [DisplayName("Offset Save Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Offset save interval must be at least 1 second.")]
    public int AgentOffsetSaveIntervalSeconds { get; set; } = 30;

    [DisplayName("Agent Name")]
    [MaxLength(120, ErrorMessage = "Agent name must be 120 characters or fewer.")]
    public string AgentName { get; set; } = DefaultAgentName;

    // Ban file sync defaults
    [DisplayName("Check Interval (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Check interval must be at least 1 second.")]
    public int BanFileSyncCheckIntervalSeconds { get; set; } = 60;

    // Moderation defaults
    [DisplayName("Hate Threshold")]
    [Range(-1, 6, ErrorMessage = "Hate threshold must be Disabled or between 0 and 6.")]
    public int ModerationHateSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Violence Threshold")]
    [Range(-1, 6, ErrorMessage = "Violence threshold must be Disabled or between 0 and 6.")]
    public int ModerationViolenceSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Sexual Threshold")]
    [Range(-1, 6, ErrorMessage = "Sexual threshold must be Disabled or between 0 and 6.")]
    public int ModerationSexualSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Self-Harm Threshold")]
    [Range(-1, 6, ErrorMessage = "Self-Harm threshold must be Disabled or between 0 and 6.")]
    public int ModerationSelfHarmSeverityThreshold { get; set; } = DisabledSeverityThreshold;

    [DisplayName("Minimum Message Length")]
    [Range(1, int.MaxValue, ErrorMessage = "Minimum message length must be at least 1.")]
    public int ModerationMinMessageLength { get; set; } = 5;

    // Events defaults
    [DisplayName("Stale Event Threshold (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Stale event threshold must be at least 1 second.")]
    public int EventsStaleThresholdSeconds { get; set; } = 120;

    [DisplayName("Player Cache Expiration (s)")]
    [Range(1, int.MaxValue, ErrorMessage = "Player cache expiration must be at least 1 second.")]
    public int EventsPlayerCacheExpirationSeconds { get; set; } = 900;

    // Broadcast defaults

    [DisplayName("Enabled")]
    public bool BroadcastsEnabled { get; set; }

    [DisplayName("Interval (seconds)")]
    [Range(1, 86400, ErrorMessage = "Broadcast interval must be between 1 and 86400 seconds.")]
    public int BroadcastsIntervalSeconds { get; set; } = GameServerEditViewModel.DefaultBroadcastIntervalSeconds;

    public List<BroadcastMessageViewModel> BroadcastMessages { get; set; } = [];

    // CoD4x plugin defaults

    [DisplayName("CoD4x Plugin Enabled")]
    public bool Cod4xPluginEnabled { get; set; }

    [DisplayName("CoD4x VPN Protection Fast Path")]
    public bool Cod4xPluginVpnProtectionEnabled { get; set; }

    [DisplayName("Plugin Root Directory")]
    [MaxLength(512, ErrorMessage = "Plugin root directory must be 512 characters or fewer.")]
    public string? Cod4xPluginRootDirectory { get; set; }

    // CoD4x power defaults

    [DisplayName("CoD4x Power Sync Enabled")]
    public bool Cod4xPowerEnabled { get; set; }

    [DisplayName("CoD4x Default Power")]
    [Range(Cod4xPowerSettingsConstants.MinPower, Cod4xPowerSettingsConstants.MaxPower,
        ErrorMessage = "Default power must be between 1 and 100.")]
    public int Cod4xPowerDefaultPower { get; set; } = Cod4xPowerSettingsConstants.DefaultPower;

    [DisplayName("CoD4x Power Tag Mappings (JSON)")]
    public string Cod4xPowerTagMappingsJson { get; set; } = "[]";

    public List<Cod4xPowerTagMappingViewModel> Cod4xPowerTagMappings { get; set; } = [];

    // CoD4x command defaults

    [DisplayName("CoD4x Command Power Enforcement Enabled")]
    public bool Cod4xCommandsEnabled { get; set; }

    public List<Cod4xCommandViewModel> Cod4xCommands { get; set; } = Cod4xSettingsViewModelHelpers.CreateDefaultCommands();

    // Server list defaults

    [DisplayName("HTML Banner")]
    public string? ServerListHtmlBanner { get; set; }

    // Legacy compatibility alias retained while the UI model is being normalized in later phases.

    public List<BroadcastMessageViewModel> FunnyMessages
    {
        get => BroadcastMessages;
        set => BroadcastMessages = value ?? [];
    }

    public ChatCommandGlobalSettingsViewModel ChatCommands { get; set; } = new();

    public WelcomeMessageGlobalSettingsViewModel WelcomeMessages { get; set; } = new();

    public VpnProtectionGlobalSettingsViewModel VpnProtection { get; set; } = new();

    public IReadOnlyList<RequiredTagOptionViewModel> AvailableRequiredTags { get; set; } = [];

    public bool IsRequiredTagsCatalogAvailable { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        SyncCod4xPowerTagMappings();

        if (BroadcastMessages is null)
            yield break;

        for (var i = 0; i < BroadcastMessages.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(BroadcastMessages[i].Message))
                yield return new ValidationResult("Broadcast message is required.", [$"BroadcastMessages[{i}].Message"]);

            if (BroadcastMessages[i].Message?.Length > MaxFunnyMessageLength)
                yield return new ValidationResult($"Broadcast message cannot exceed {MaxFunnyMessageLength} characters.", [$"BroadcastMessages[{i}].Message"]);
        }

        foreach (var validationResult in ChatCommands.Validate(validationContext))
        {
            yield return validationResult;
        }

        foreach (var validationResult in WelcomeMessages.Validate(validationContext))
        {
            yield return validationResult;
        }

        foreach (var validationResult in VpnProtection.Validate(validationContext))
        {
            yield return validationResult;
        }

        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Cod4xPowerTagMappings.Count; i++)
        {
            var mapping = Cod4xPowerTagMappings[i];
            if (string.IsNullOrWhiteSpace(mapping.Tag))
            {
                yield return new ValidationResult($"CoD4x power mapping at index {i} is missing a tag.", [nameof(Cod4xPowerTagMappingsJson)]);
                continue;
            }

            if (!seenTags.Add(mapping.Tag.Trim()))
            {
                yield return new ValidationResult($"CoD4x power tag '{mapping.Tag}' is duplicated.", [nameof(Cod4xPowerTagMappingsJson)]);
            }

            if (mapping.Power is < 0 or > Cod4xPowerSettingsConstants.MaxPower)
            {
                yield return new ValidationResult(
                    $"CoD4x power for tag '{mapping.Tag}' must be between 0 and {Cod4xPowerSettingsConstants.MaxPower}.",
                    [nameof(Cod4xPowerTagMappingsJson)]);
            }
        }

        foreach (var command in Cod4xCommands)
        {
            if (command.MinPower is < 1 or > 100)
            {
                yield return new ValidationResult("Each CoD4x command minimum power must be between 1 and 100.", [nameof(Cod4xCommands)]);
                break;
            }
        }
    }

    public void ApplyAvailableRequiredTags(
        IReadOnlyList<RequiredTagOptionViewModel> requiredTags,
        bool requiredTagsCatalogAvailable = true)
    {
        IsRequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        AvailableRequiredTags = requiredTags;
        SyncCod4xPowerTagMappings();
        ChatCommands.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        ChatCommands.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        WelcomeMessages.AllowedRequiredTags = requiredTags.Select(static option => option.Name).ToArray();
        WelcomeMessages.RequiredTagsCatalogAvailable = requiredTagsCatalogAvailable;
        VpnProtection.AllowedExcludedPlayerTags = requiredTags.Select(static option => option.Name).ToArray();
        VpnProtection.ExcludedPlayerTagsCatalogAvailable = requiredTagsCatalogAvailable;
    }

    public void SyncCod4xPowerTagMappings()
    {
        if (!IsRequiredTagsCatalogAvailable)
        {
            return;
        }

        Cod4xPowerTagMappings = Cod4xSettingsViewModelHelpers.BuildPowerTagMappings(
            AvailableRequiredTags,
            Cod4xPowerTagMappings,
            Cod4xPowerTagMappingsJson);

        Cod4xPowerTagMappingsJson = Cod4xSettingsViewModelHelpers.SerializePowerMappingsJson(
            Cod4xSettingsViewModelHelpers.BuildPowerTagMappingsForPersistence(Cod4xPowerTagMappings, Cod4xPowerTagMappingsJson));
    }

    public static IReadOnlyList<SelectListItem> BuildSeverityOptions(bool includeInheritOption)
    {
        var options = new List<SelectListItem>();

        if (includeInheritOption)
        {
            options.Add(new SelectListItem("Use global default", ""));
        }

        options.Add(new SelectListItem("Disabled", DisabledSeverityThreshold.ToString()));

        for (var i = 0; i <= 6; i++)
        {
            options.Add(new SelectListItem(i.ToString(), i.ToString()));
        }

        return options;
    }
}
