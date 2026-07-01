using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xCommands;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPower;

namespace XtremeIdiots.Portal.Web.ViewModels;

public static class Cod4xSettingsViewModelHelpers
{
    private readonly static JsonSerializerOptions editorJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly static JsonSerializerOptions parserJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<Cod4xCommandViewModel> CreateDefaultCommands()
    {
        return
        [
            .. Cod4xCommandSettingsConstants.BuiltInCommandMinPowerDefaults.Select(static command => new Cod4xCommandViewModel
            {
                Name = command.Key,
                Enabled = true,
                MinPower = command.Value
            })
        ];
    }

    public static void ApplyCommandOverrides(
        IList<Cod4xCommandViewModel> commands,
        IReadOnlyDictionary<string, Cod4xCommandSettingsEntry>? commandOverrides)
    {
        if (commandOverrides is null || commandOverrides.Count == 0)
        {
            return;
        }

        var commandLookup = commands
            .Where(static command => !string.IsNullOrWhiteSpace(command.Name))
            .ToDictionary(static command => command.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var (rawCommandName, settings) in commandOverrides)
        {
            var canonicalName = ResolveCanonicalCommandName(rawCommandName);
            if (!commandLookup.TryGetValue(canonicalName, out var command))
            {
                continue;
            }

            if (settings.Enabled.HasValue)
            {
                command.Enabled = settings.Enabled.Value;
            }

            if (settings.MinPower.HasValue)
            {
                command.MinPower = settings.MinPower.Value;
            }
        }
    }

    public static Dictionary<string, Cod4xCommandSettingsEntry> BuildCommandDictionary(IEnumerable<Cod4xCommandViewModel>? commands)
    {
        var output = new Dictionary<string, Cod4xCommandSettingsEntry>(StringComparer.OrdinalIgnoreCase);
        if (commands is null)
        {
            return output;
        }

        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                continue;
            }

            var canonicalName = ResolveCanonicalCommandName(command.Name);
            output[canonicalName] = new Cod4xCommandSettingsEntry
            {
                Enabled = command.Enabled,
                MinPower = command.MinPower
            };
        }

        return output;
    }

    public static bool TryParsePowerMappingsJson(string? mappingsJson, out List<Cod4xPowerTagMapping> mappings)
    {
        mappings = [];
        if (string.IsNullOrWhiteSpace(mappingsJson))
        {
            return true;
        }

        try
        {
            var parsedMappings = JsonSerializer.Deserialize<List<Cod4xPowerTagMapping>>(mappingsJson, parserJsonOptions) ?? [];
            mappings =
            [
                .. parsedMappings.Where(static mapping => mapping is not null)
            ];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static List<Cod4xPowerTagMappingViewModel> BuildPowerTagMappings(
        IReadOnlyList<RequiredTagOptionViewModel> availableTags,
        IEnumerable<Cod4xPowerTagMappingViewModel>? currentMappings,
        string? mappingsJson)
    {
        var mappingLookup = BuildPowerTagMappingLookup(currentMappings, mappingsJson);

        return
        [
            .. availableTags
                .Where(static tag => !string.IsNullOrWhiteSpace(tag.Name))
                .Select(static tag => tag.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag => new Cod4xPowerTagMappingViewModel
                {
                    Tag = tag,
                    Power = mappingLookup.TryGetValue(tag, out var power)
                        ? power
                        : 0
                })
        ];
    }

    public static List<Cod4xPowerTagMapping> BuildPowerTagMappingsForPersistence(
        IEnumerable<Cod4xPowerTagMappingViewModel>? mappings,
        string? fallbackMappingsJson = null)
    {
        var outputByTag = new Dictionary<string, Cod4xPowerTagMapping>(StringComparer.OrdinalIgnoreCase);
        var hasEditorMappings = false;
        var fallbackByTag = new Dictionary<string, Cod4xPowerTagMapping>(StringComparer.OrdinalIgnoreCase);

        if (TryParsePowerMappingsJson(fallbackMappingsJson, out var parsedFallbackMappings))
        {
            foreach (var mapping in parsedFallbackMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.Tag))
                {
                    continue;
                }

                var normalizedTag = mapping.Tag.Trim();
                fallbackByTag[normalizedTag] = new Cod4xPowerTagMapping
                {
                    Tag = normalizedTag,
                    Power = mapping.Power,
                    Enabled = mapping.Enabled
                };
            }
        }

        foreach (var mapping in mappings ?? [])
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Tag))
            {
                continue;
            }

            hasEditorMappings = true;

            var normalizedTag = mapping.Tag.Trim();
            var normalizedPower = Math.Clamp(mapping.Power, 0, Cod4xPowerSettingsConstants.MaxPower);
            if (normalizedPower <= 0)
            {
                continue;
            }

            var enabled = true;
            if (fallbackByTag.TryGetValue(normalizedTag, out var existingMapping))
            {
                enabled = existingMapping.Enabled;
            }

            outputByTag[normalizedTag] = new Cod4xPowerTagMapping
            {
                Tag = normalizedTag,
                Power = normalizedPower,
                Enabled = enabled
            };
        }

        if (!hasEditorMappings)
        {
            foreach (var mapping in fallbackByTag.Values)
            {
                if (string.IsNullOrWhiteSpace(mapping.Tag) || !mapping.Power.HasValue)
                {
                    continue;
                }

                var normalizedTag = mapping.Tag.Trim();
                var normalizedPower = Math.Clamp(mapping.Power.Value, 0, Cod4xPowerSettingsConstants.MaxPower);
                if (normalizedPower <= 0)
                {
                    continue;
                }

                outputByTag[normalizedTag] = new Cod4xPowerTagMapping
                {
                    Tag = normalizedTag,
                    Power = normalizedPower,
                    Enabled = mapping.Enabled
                };
            }
        }

        return
        [
            .. outputByTag.Values
        ];
    }

    public static string SerializePowerMappingsJson(IEnumerable<Cod4xPowerTagMapping>? mappings)
    {
        var safeMappings = (mappings ?? []).Where(static mapping => mapping is not null);
        return JsonSerializer.Serialize(safeMappings, editorJsonOptions);
    }

    private static Dictionary<string, int> BuildPowerTagMappingLookup(
        IEnumerable<Cod4xPowerTagMappingViewModel>? currentMappings,
        string? mappingsJson)
    {
        var mappingLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hasCurrentMappings = false;

        foreach (var mapping in currentMappings ?? [])
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Tag))
            {
                continue;
            }

            hasCurrentMappings = true;
            mappingLookup[mapping.Tag.Trim()] = Math.Clamp(mapping.Power, 0, Cod4xPowerSettingsConstants.MaxPower);
        }

        if (hasCurrentMappings)
        {
            return mappingLookup;
        }

        if (!TryParsePowerMappingsJson(mappingsJson, out var parsedMappings))
        {
            return mappingLookup;
        }

        foreach (var mapping in parsedMappings)
        {
            if (mapping is null || string.IsNullOrWhiteSpace(mapping.Tag))
            {
                continue;
            }

            mappingLookup[mapping.Tag.Trim()] = Math.Clamp(mapping.Power.GetValueOrDefault(), 0, Cod4xPowerSettingsConstants.MaxPower);
        }

        return mappingLookup;
    }

    public static string ResolveCanonicalCommandName(string? commandName)
    {
        var normalizedName = commandName?.Trim() ?? string.Empty;
        return Cod4xCommandSettingsConstants.BuiltInCommandAliases.TryGetValue(normalizedName, out var canonicalName)
            ? canonicalName
            : normalizedName;
    }
}

public sealed class Cod4xCommandViewModel
{
    public string Name { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;

    [DisplayName("Minimum Power")]
    [Range(Cod4xCommandSettingsConstants.MinPower, Cod4xCommandSettingsConstants.MaxPower,
        ErrorMessage = "Minimum power must be between 1 and 100.")]
    public int MinPower { get; set; } = Cod4xCommandSettingsConstants.MinPower;
}

public sealed class Cod4xPowerTagMappingViewModel
{
    public string Tag { get; set; } = string.Empty;

    [DisplayName("Power")]
    [Range(0, Cod4xPowerSettingsConstants.MaxPower,
        ErrorMessage = "Power must be between 0 and 100.")]
    public int Power { get; set; }
}
