using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using XtremeIdiots.Portal.Server.Events.Processor.App.Commands;

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
    public string DefaultRequiredTags { get; set; } = string.Empty;

    [DisplayName("Default Required Claims")]
    public string DefaultRequiredClaims { get; set; } = string.Empty;

    public List<ChatCommandGlobalEntryViewModel> Commands { get; set; } =
        ChatCommandDescriptorCatalog.All
            .Select(static descriptor => new ChatCommandGlobalEntryViewModel
            {
                Name = descriptor.Name,
                Prefix = descriptor.Prefix,
                Usage = descriptor.Usage,
                Description = descriptor.Description,
                IsMutating = descriptor.IsMutating
            })
            .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in ChatCommandViewModelValidation.ValidateCommands(Commands))
        {
            yield return validationResult;
        }
    }
}

public class ChatCommandServerSettingsViewModel : IValidatableObject
{
    public List<ChatCommandServerEntryViewModel> Commands { get; set; } =
        ChatCommandDescriptorCatalog.All
            .Select(static descriptor => new ChatCommandServerEntryViewModel
            {
                Name = descriptor.Name,
                Prefix = descriptor.Prefix,
                Usage = descriptor.Usage,
                Description = descriptor.Description,
                IsMutating = descriptor.IsMutating
            })
            .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in ChatCommandViewModelValidation.ValidateCommands(Commands))
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

    [DisplayName("Enabled")]
    public bool? Enabled { get; set; }

    [DisplayName("Freshness (s)")]
    [Range(0, int.MaxValue, ErrorMessage = "Freshness must be 0 or greater.")]
    public int? FreshnessSeconds { get; set; }

    [DisplayName("Required Tags")]
    public string RequiredTags { get; set; } = string.Empty;

    [DisplayName("Required Claims")]
    public string RequiredClaims { get; set; } = string.Empty;

    public List<BroadcastMessageViewModel> Messages { get; set; } = [];
}

public sealed class ChatCommandGlobalEntryViewModel : ChatCommandEntryViewModelBase
{
}

public sealed class ChatCommandServerEntryViewModel : ChatCommandEntryViewModelBase
{
    [DisplayName("Use Global Enabled")]
    public bool UseGlobalEnabled { get; set; } = true;

    [DisplayName("Use Global Freshness")]
    public bool UseGlobalFreshness { get; set; } = true;

    [DisplayName("Use Global Required Tags")]
    public bool UseGlobalRequiredTags { get; set; } = true;

    [DisplayName("Use Global Required Claims")]
    public bool UseGlobalRequiredClaims { get; set; } = true;

    [DisplayName("Use Global Messages")]
    public bool UseGlobalMessages { get; set; } = true;
}

internal static class ChatCommandViewModelValidation
{
    public static IEnumerable<ValidationResult> ValidateCommands<T>(IReadOnlyList<T>? commands)
        where T : ChatCommandEntryViewModelBase
    {
        if (commands is null)
        {
            yield break;
        }

        for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
        {
            var command = commands[commandIndex];
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
}

internal static class ChatCommandSettingsJsonMapper
{
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
            target.DefaultRequiredClaims = string.Join(", ", GetStringArray(defaultsElement, "requiredClaims"));
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
        defaults["requiredTags"] = SplitCsv(model.DefaultRequiredTags);
        defaults["requiredClaims"] = SplitCsv(model.DefaultRequiredClaims);

        payload["schemaVersion"] = ChatCommandSettingsConstants.SupportedSchemaVersion;
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
        payload["schemaVersion"] = ChatCommandSettingsConstants.SupportedSchemaVersion;
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

        var requiredClaims = SplitCsv(command.RequiredClaims);
        if (requiredClaims.Length > 0)
        {
            payload["requiredClaims"] = requiredClaims;
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

        if (!command.UseGlobalEnabled && command.Enabled.HasValue)
        {
            payload["enabled"] = command.Enabled.Value;
        }

        if (!command.UseGlobalFreshness && command.FreshnessSeconds.HasValue)
        {
            payload["freshnessSeconds"] = command.FreshnessSeconds.Value;
        }

        if (!command.UseGlobalRequiredTags)
        {
            payload["requiredTags"] = SplitCsv(command.RequiredTags);
        }

        if (!command.UseGlobalRequiredClaims)
        {
            payload["requiredClaims"] = SplitCsv(command.RequiredClaims);
        }

        if (!command.UseGlobalMessages && command.Messages.Count > 0)
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
                command.Enabled = GetNullableBoolProperty(commandElement, "enabled");
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.UseGlobalEnabled = false;
                }
            }

            if (commandElement.TryGetProperty("freshnessSeconds", out _))
            {
                command.FreshnessSeconds = GetNullableIntProperty(commandElement, "freshnessSeconds");
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.UseGlobalFreshness = false;
                }
            }

            if (commandElement.TryGetProperty("requiredTags", out _))
            {
                command.RequiredTags = string.Join(", ", GetStringArray(commandElement, "requiredTags"));
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.UseGlobalRequiredTags = false;
                }
            }

            if (commandElement.TryGetProperty("requiredClaims", out _))
            {
                command.RequiredClaims = string.Join(", ", GetStringArray(commandElement, "requiredClaims"));
                if (isServerOverride && command is ChatCommandServerEntryViewModel serverEntry)
                {
                    serverEntry.UseGlobalRequiredClaims = false;
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
                    serverEntry.UseGlobalMessages = false;
                }
            }
        }
    }

    private static string[] SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
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
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool? GetNullableBoolProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

#pragma warning restore IDE0305