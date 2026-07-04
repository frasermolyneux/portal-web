using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;

namespace XtremeIdiots.Portal.Web.ViewModels;

#pragma warning disable IDE0305

public static class ChatCommandSettingsViewModelConstants
{
    public const int DefaultFreshnessSeconds = ChatCommandSettingsConstants.HardcodedDefaultFreshnessSeconds;
    public const int ReadOnlyFreshnessSeconds = ChatCommandSettingsConstants.HardcodedReadOnlyFreshnessSeconds;
    public const int MutatingFreshnessSeconds = ChatCommandSettingsConstants.HardcodedMutatingFreshnessSeconds;
    public const int MaxCommandMessageLength = 120;
}

public class ChatCommandGlobalSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled by Default")]
    public bool DefaultsEnabled { get; set; } = true;

    [DisplayName("Default Freshness (s)")]
    [Range(0, int.MaxValue, ErrorMessage = "Default freshness must be 0 or greater.")]
    public int DefaultFreshnessSeconds { get; set; } = ChatCommandSettingsViewModelConstants.DefaultFreshnessSeconds;

    [DisplayName("Read-Only Freshness (s)")]
    [Range(0, int.MaxValue, ErrorMessage = "Read-only freshness must be 0 or greater.")]
    public int ReadOnlyFreshnessSeconds { get; set; } = ChatCommandSettingsViewModelConstants.ReadOnlyFreshnessSeconds;

    [DisplayName("Mutating Freshness (s)")]
    [Range(0, int.MaxValue, ErrorMessage = "Mutating freshness must be 0 or greater.")]
    public int MutatingFreshnessSeconds { get; set; } = ChatCommandSettingsViewModelConstants.MutatingFreshnessSeconds;

    [DisplayName("Default Required Tags")]
    public string? DefaultRequiredTags { get; set; } = string.Empty;

    public IReadOnlyList<string> AllowedRequiredTags { get; set; } = [];

    // Defaults to false: the tags catalog is not loaded during model binding, so required-tag validation
    // must stay dormant until ApplyAvailableRequiredTags supplies the real allow-list. A true default would
    // flag every assigned tag as unavailable during binding and silently block the settings save.
    public bool RequiredTagsCatalogAvailable { get; set; }

    public List<ChatCommandGlobalEntryViewModel> Commands { get; set; } =
        ChatCommandDescriptorCatalog.All
            .Select(static descriptor => new ChatCommandGlobalEntryViewModel
            {
                Name = descriptor.Name,
                Prefix = descriptor.Prefix,
                Usage = descriptor.Usage,
                Description = descriptor.Description,
                IsMutating = descriptor.IsMutating,
                Aliases = descriptor.Aliases?.ToList() ?? []
            })
            .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in ChatCommandViewModelValidation.ValidateCommands(
            Commands,
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return validationResult;
        }

        foreach (var invalidTag in ChatCommandViewModelValidation.GetInvalidTags(
            DefaultRequiredTags,
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return new ValidationResult(
                $"Default required tag '{invalidTag}' is not available.",
                [nameof(DefaultRequiredTags)]);
        }
    }
}

public class ChatCommandServerSettingsViewModel : IValidatableObject
{
    public IReadOnlyList<string> AllowedRequiredTags { get; set; } = [];

    // Defaults to false: the tags catalog is not loaded during model binding (see the global variant).
    public bool RequiredTagsCatalogAvailable { get; set; }

    public List<ChatCommandServerEntryViewModel> Commands { get; set; } =
        ChatCommandDescriptorCatalog.All
            .Select(static descriptor => new ChatCommandServerEntryViewModel
            {
                Name = descriptor.Name,
                Prefix = descriptor.Prefix,
                Usage = descriptor.Usage,
                Description = descriptor.Description,
                IsMutating = descriptor.IsMutating,
                Aliases = descriptor.Aliases?.ToList() ?? []
            })
            .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in ChatCommandViewModelValidation.ValidateCommands(
            Commands,
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return validationResult;
        }
    }
}

public abstract class ChatCommandEntryViewModelBase
{
    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string Usage { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsMutating { get; set; }

    public List<string> Aliases { get; set; } = [];

    [DisplayName("Enabled")]
    public bool? Enabled { get; set; }

    [DisplayName("Freshness (s)")]
    [Range(0, int.MaxValue, ErrorMessage = "Freshness must be 0 or greater.")]
    public int? FreshnessSeconds { get; set; }

    [DisplayName("Required Tags")]
    public string? RequiredTags { get; set; } = string.Empty;

    public List<BroadcastMessageViewModel> Messages { get; set; } = [];
}

public sealed class ChatCommandGlobalEntryViewModel : ChatCommandEntryViewModelBase
{
}

public sealed class ChatCommandServerEntryViewModel : ChatCommandEntryViewModelBase
{
    [DisplayName("Enabled Override")]
    public TriStateOverrideValue EnabledOverride { get; set; } = TriStateOverrideValue.Inherit();

    [DisplayName("Enabled Override")]
    public bool OverrideEnabled
    {
        get => EnabledOverride?.Value is not null;
        set => Enabled = value ? Enabled ?? false : null;
    }

    [DisplayName("Enabled Override")]
    public new bool? Enabled
    {
        get => EnabledOverride?.Value;
        set
        {
            base.Enabled = value;
            EnabledOverride = TriStateOverrideValue.From(value);
        }
    }

    [DisplayName("Freshness Override")]
    public bool OverrideFreshness { get; set; }

    [DisplayName("Required Tags Override")]
    public bool OverrideRequiredTags { get; set; }

    [DisplayName("Messages Override")]
    public bool OverrideMessages { get; set; }
}

internal static class ChatCommandViewModelValidation
{
    public static IEnumerable<ValidationResult> ValidateCommands<T>(
        IReadOnlyList<T>? commands,
        IReadOnlyList<string> allowedRequiredTags,
        bool requiredTagsCatalogAvailable)
        where T : ChatCommandEntryViewModelBase
    {
        if (commands is null)
        {
            yield break;
        }

        var allowedTagSet = allowedRequiredTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            var command = commands[commandIndex];

            var validateRequiredTags = command is not ChatCommandServerEntryViewModel serverCommand
                || serverCommand.OverrideRequiredTags;

            if (validateRequiredTags)
            {
                foreach (var invalidTag in GetInvalidTags(
                    command.RequiredTags,
                    allowedTagSet,
                    requiredTagsCatalogAvailable))
                {
                    yield return new ValidationResult(
                        $"Required tag '{invalidTag}' is not available.",
                        [$"Commands[{commandIndex}].RequiredTags"]);
                }
            }

            if (command.Messages is null)
            {
                continue;
            }

            for (var messageIndex = 0; messageIndex < command.Messages.Count; messageIndex++)
            {
                var message = command.Messages[messageIndex];
                if ((message.Message?.Length ?? 0) > ChatCommandSettingsViewModelConstants.MaxCommandMessageLength)
                {
                    yield return new ValidationResult(
                        $"Command message cannot exceed {ChatCommandSettingsViewModelConstants.MaxCommandMessageLength} characters.",
                        [$"Commands[{commandIndex}].Messages[{messageIndex}].Message"]);
                }
            }
        }
    }

    public static IEnumerable<string> GetInvalidTags(
        string? csvValue,
        IReadOnlyList<string> allowedRequiredTags,
        bool requiredTagsCatalogAvailable)
    {
        var allowedTagSet = allowedRequiredTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetInvalidTags(csvValue, allowedTagSet, requiredTagsCatalogAvailable);
    }

    private static IEnumerable<string> GetInvalidTags(
        string? csvValue,
        HashSet<string> allowedTagSet,
        bool requiredTagsCatalogAvailable)
    {
        if (!requiredTagsCatalogAvailable)
        {
            yield break;
        }

        foreach (var selectedTag in RequiredTagsSelection.SplitCsv(csvValue))
        {
            if (!allowedTagSet.Contains(selectedTag))
            {
                yield return selectedTag;
            }
        }
    }
}

internal static class ChatCommandSettingsJsonMapper
{
    public static void PopulateGlobal(ChatCommandGlobalSettingsViewModel target, ChatCommandSettingsDocument document)
    {
        var defaults = document.Defaults;
        if (defaults is not null)
        {
            target.DefaultsEnabled = defaults.Enabled ?? target.DefaultsEnabled;

            if (defaults.FreshnessSeconds is not null)
            {
                target.DefaultFreshnessSeconds = defaults.FreshnessSeconds.Default ?? target.DefaultFreshnessSeconds;
                target.ReadOnlyFreshnessSeconds = defaults.FreshnessSeconds.ReadOnly ?? target.ReadOnlyFreshnessSeconds;
                target.MutatingFreshnessSeconds = defaults.FreshnessSeconds.Mutating ?? target.MutatingFreshnessSeconds;
            }

            target.DefaultRequiredTags = string.Join(", ", (defaults.RequiredTags ?? [])
                .Where(static item => !string.IsNullOrWhiteSpace(item)));
        }

        PopulateTypedCommandEntries(target.Commands, document.Commands, isServerOverride: false);
    }

    public static void PopulateServer(ChatCommandServerSettingsViewModel target, ChatCommandSettingsDocument document)
    {
        PopulateTypedCommandEntries(target.Commands, document.Commands, isServerOverride: true);
    }

    public static void PopulateGlobal(ChatCommandGlobalSettingsViewModel target, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("defaults", out var defaultsElement) && defaultsElement.ValueKind == JsonValueKind.Object)
        {
            target.DefaultsEnabled = GetBoolProperty(defaultsElement, "enabled", target.DefaultsEnabled);

            if (defaultsElement.TryGetProperty("freshnessSeconds", out var freshnessElement) && freshnessElement.ValueKind == JsonValueKind.Object)
            {
                target.DefaultFreshnessSeconds = GetIntProperty(freshnessElement, "default", target.DefaultFreshnessSeconds);
                target.ReadOnlyFreshnessSeconds = GetIntProperty(freshnessElement, "readOnly", target.ReadOnlyFreshnessSeconds);
                target.MutatingFreshnessSeconds = GetIntProperty(freshnessElement, "mutating", target.MutatingFreshnessSeconds);
            }

            target.DefaultRequiredTags = string.Join(", ", GetStringArray(defaultsElement, "requiredTags"));
        }

        if (root.TryGetProperty("commands", out var commandsElement) && commandsElement.ValueKind == JsonValueKind.Object)
        {
            PopulateCommandEntries(target.Commands, commandsElement, isServerOverride: false);
        }
    }

    public static void PopulateServer(ChatCommandServerSettingsViewModel target, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("commands", out var commandsElement) && commandsElement.ValueKind == JsonValueKind.Object)
        {
            PopulateCommandEntries(target.Commands, commandsElement, isServerOverride: true);
        }
    }

    public static string BuildGlobalConfigurationJson(ChatCommandGlobalSettingsViewModel model)
    {
        var commands = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in model.Commands)
        {
            var commandObject = BuildGlobalCommandObject(command);
            if (commandObject.Count > 0)
            {
                commands[command.Name] = commandObject;
            }
        }

        Dictionary<string, object?> payload = [];
        Dictionary<string, object?> defaults = [];
        Dictionary<string, object?> freshness = [];

        freshness["default"] = model.DefaultFreshnessSeconds;
        freshness["readOnly"] = model.ReadOnlyFreshnessSeconds;
        freshness["mutating"] = model.MutatingFreshnessSeconds;

        defaults["enabled"] = model.DefaultsEnabled;
        defaults["freshnessSeconds"] = freshness;
        var defaultRequiredTags = SplitCsv(model.DefaultRequiredTags);
        if (defaultRequiredTags.Length > 0)
        {
            defaults["requiredTags"] = defaultRequiredTags;
        }

        payload["schemaVersion"] = ChatCommandSettingsConstants.SchemaVersion;
        payload["defaults"] = defaults;
        payload["commands"] = commands;

        return JsonSerializer.Serialize(payload);
    }

    public static string BuildServerConfigurationJson(ChatCommandServerSettingsViewModel model)
    {
        var commands = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in model.Commands)
        {
            var commandObject = BuildServerCommandObject(command);
            if (commandObject.Count > 0)
            {
                commands[command.Name] = commandObject;
            }
        }

        Dictionary<string, object?> payload = [];
        payload["schemaVersion"] = ChatCommandSettingsConstants.SchemaVersion;
        payload["commands"] = commands;

        return JsonSerializer.Serialize(payload);
    }

    private static Dictionary<string, object?> BuildGlobalCommandObject(ChatCommandGlobalEntryViewModel command)
    {
        Dictionary<string, object?> payload = [];

        if (command.Enabled.HasValue)
        {
            payload["enabled"] = command.Enabled.Value;
        }

        if (command.FreshnessSeconds.HasValue)
        {
            payload["freshnessSeconds"] = command.FreshnessSeconds.Value;
        }

        var requiredTags = SplitCsv(command.RequiredTags);
        if (requiredTags.Length > 0)
        {
            payload["requiredTags"] = requiredTags;
        }

        if (command.Messages.Count > 0)
        {
            var messagePayloads = command.Messages.Select(m =>
            {
                Dictionary<string, object?> messagePayload = [];
                messagePayload["message"] = m.Message;
                messagePayload["enabled"] = m.Enabled;
                return messagePayload;
            }).ToArray();

            Dictionary<string, object?> settingsPayload = [];
            settingsPayload["messages"] = messagePayloads;
            payload["settings"] = settingsPayload;
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildServerCommandObject(ChatCommandServerEntryViewModel command)
    {
        Dictionary<string, object?> payload = [];

        if (command.EnabledOverride.Value.HasValue)
        {
            payload["enabled"] = command.EnabledOverride.Value;
        }

        if (command.OverrideFreshness && command.FreshnessSeconds.HasValue)
        {
            payload["freshnessSeconds"] = command.FreshnessSeconds.Value;
        }

        if (command.OverrideRequiredTags)
        {
            payload["requiredTags"] = SplitCsv(command.RequiredTags);
        }

        if (command.OverrideMessages && command.Messages.Count > 0)
        {
            var messagePayloads = command.Messages.Select(m =>
            {
                Dictionary<string, object?> messagePayload = [];
                messagePayload["message"] = m.Message;
                messagePayload["enabled"] = m.Enabled;
                return messagePayload;
            }).ToArray();

            Dictionary<string, object?> settingsPayload = [];
            settingsPayload["messages"] = messagePayloads;
            payload["settings"] = settingsPayload;
        }

        return payload;
    }

    private static void PopulateCommandEntries<T>(IReadOnlyList<T> commands, JsonElement commandsElement, bool isServerOverride)
        where T : ChatCommandEntryViewModelBase
    {
        foreach (var command in commands)
        {
            if (!commandsElement.TryGetProperty(command.Name, out var commandElement) || commandElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (commandElement.TryGetProperty("enabled", out _))
            {
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.EnabledOverride = TriStateOverrideValue.From(GetNullableBoolProperty(commandElement, "enabled"));
                }
                else
                {
                    command.Enabled = GetNullableBoolProperty(commandElement, "enabled");
                }
            }

            if (commandElement.TryGetProperty("freshnessSeconds", out _))
            {
                command.FreshnessSeconds = GetNullableIntProperty(commandElement, "freshnessSeconds");
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideFreshness = true;
                }
            }

            if (commandElement.TryGetProperty("requiredTags", out _))
            {
                command.RequiredTags = string.Join(", ", GetStringArray(commandElement, "requiredTags"));
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideRequiredTags = true;
                }
            }

            if (commandElement.TryGetProperty("settings", out var settingsElement) &&
                settingsElement.ValueKind == JsonValueKind.Object &&
                settingsElement.TryGetProperty("messages", out var messagesElement) &&
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                command.Messages = messagesElement
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new BroadcastMessageViewModel
                    {
                        Message = GetStringProperty(item, "message") ?? string.Empty,
                        Enabled = GetBoolProperty(item, "enabled", true)
                    })
                    .ToList();

                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideMessages = true;
                }
            }
        }
    }

    private static void PopulateTypedCommandEntries<T>(
        IReadOnlyList<T> commands,
        Dictionary<string, ChatCommandSettingsEntry>? commandSettings,
        bool isServerOverride)
        where T : ChatCommandEntryViewModelBase
    {
        if (commandSettings is null || commandSettings.Count == 0)
        {
            return;
        }

        foreach (var command in commands)
        {
            if (!commandSettings.TryGetValue(command.Name, out var entry) || entry is null)
            {
                continue;
            }

            if (entry.Enabled.HasValue)
            {
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.EnabledOverride = TriStateOverrideValue.From(entry.Enabled);
                }
                else
                {
                    command.Enabled = entry.Enabled;
                }
            }

            if (entry.FreshnessSeconds.HasValue)
            {
                command.FreshnessSeconds = entry.FreshnessSeconds;
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideFreshness = true;
                }
            }

            if (entry.RequiredTags is not null)
            {
                command.RequiredTags = string.Join(", ", entry.RequiredTags.Where(static item => !string.IsNullOrWhiteSpace(item)));
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideRequiredTags = true;
                }
            }

            if (entry.Settings is JsonElement settingsElement &&
                settingsElement.ValueKind == JsonValueKind.Object &&
                settingsElement.TryGetProperty("messages", out var messagesElement) &&
                messagesElement.ValueKind == JsonValueKind.Array)
            {
                command.Messages = messagesElement
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new BroadcastMessageViewModel
                    {
                        Message = GetStringProperty(item, "message") ?? string.Empty,
                        Enabled = GetBoolProperty(item, "enabled", true)
                    })
                    .ToList();

                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.OverrideMessages = true;
                }
            }
        }
    }

    private static string[] SplitCsv(string? value)
    {
        return RequiredTagsSelection.SplitCsv(value);
    }

    private static string[] GetStringArray(JsonElement root, string propertyName)
    {
        return !root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array
            ? []
            : property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static int? GetNullableIntProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool GetBoolProperty(JsonElement root, string propertyName, bool defaultValue)
    {
        return !root.TryGetProperty(propertyName, out var property)
            ? defaultValue
            : property.ValueKind switch
            {
                JsonValueKind.Undefined => defaultValue,
                JsonValueKind.Object => defaultValue,
                JsonValueKind.Array => defaultValue,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
                JsonValueKind.String => defaultValue,
                JsonValueKind.Number => defaultValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => defaultValue,
                _ => defaultValue
            };
    }

    private static bool? GetNullableBoolProperty(JsonElement root, string propertyName)
    {
        return !root.TryGetProperty(propertyName, out var property)
            ? null
            : property.ValueKind switch
            {
                JsonValueKind.Undefined => null,
                JsonValueKind.Object => null,
                JsonValueKind.Array => null,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
                JsonValueKind.String => null,
                JsonValueKind.Number => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => null
            };
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

internal sealed record ChatCommandDescriptor(
    string Name,
    string Prefix,
    string Usage,
    string Description,
    bool IsMutating,
    IReadOnlyList<string>? Aliases = null);

internal static class ChatCommandDescriptorCatalog
{
    public static ChatCommandDescriptor Commands { get; } = new(
        "commands",
        "!commands",
        "!commands",
        "List all available chat commands.",
        false,
        ["!help"]);

    public static ChatCommandDescriptor Register { get; } = new(
        "register",
        "!register",
        "!register CODE",
        "Link your in-game identity to a portal profile using an activation code.",
        true);

    public static ChatCommandDescriptor WhoAmI { get; } = new(
        "whoami",
        "!whoami",
        "!whoami",
        "Show your currently linked forum account.",
        false);

    public static ChatCommandDescriptor Fu { get; } = new(
        "fu",
        "!fu",
        "!fu <player name>",
        "Send a random funny message to a player.",
        true);

    public static ChatCommandDescriptor Like { get; } = new(
        "like",
        "!like",
        "!like <player name>",
        "Send a positive reaction message to a player.",
        true);

    public static ChatCommandDescriptor Dislike { get; } = new(
        "dislike",
        "!dislike",
        "!dislike <player name>",
        "Send a negative reaction message to a player.",
        true);

    public static IReadOnlyList<ChatCommandDescriptor> All { get; } =
    [
        Commands,
        Register,
        WhoAmI,
        Fu,
        Like,
        Dislike
    ];
}

#pragma warning restore IDE0305