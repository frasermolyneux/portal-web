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

    public static string SerializePowerMappingsJson(IEnumerable<Cod4xPowerTagMapping>? mappings)
    {
        var safeMappings = (mappings ?? []).Where(static mapping => mapping is not null);
        return JsonSerializer.Serialize(safeMappings, editorJsonOptions);
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
