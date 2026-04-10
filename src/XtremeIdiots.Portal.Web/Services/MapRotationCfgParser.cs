using System.Text.RegularExpressions;

namespace XtremeIdiots.Portal.Web.Services;

public record ParsedRotation(
    string Title,
    string GameMode,
    List<string> MapNames,
    string ConfigVariableName,
    bool IsActive,
    string? Author,
    string? DateText,
    string? RawComment);

public static partial class MapRotationCfgParser
{
    // Matches: set sv_maprotation "..." or //set sv_maprotation "..."
    // Captures: optional comment prefix, variable name, quoted value
    [GeneratedRegex(@"^(\s*//\s*)?set\s+(\S+)\s+""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex SetLineRegex();

    // Known rotation variable names (case-insensitive prefix matching)
    private readonly static string[] rotationVariablePrefixes =
    [
        "sv_maprotation",
        "sv_maprotation_",
        "scr_aacp_maps_",
        "scr_small_rotation",
        "scr_med_rotation",
        "scr_large_rotation"
    ];

    [GeneratedRegex(@"(Rotation\s*#?\d+\w?)", RegexOptions.IgnoreCase)]
    private static partial Regex RotationNameRegex();

    [GeneratedRegex(@"(?:by|BY)\s+(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorRegex();

    [GeneratedRegex(@"(\d{1,2}/\d{1,2}/\d{2,4}|\d{4}-\d{1,2}-\d{1,2}|\d{1,2}\s+\d{1,2}\s+\d{2,4})")]
    private static partial Regex DateRegex();

    public static List<ParsedRotation> Parse(string cfgContent)
    {
        var lines = cfgContent.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var rotations = new List<ParsedRotation>();
        var continuationParts = new Dictionary<string, List<(int Index, string Value, bool IsActive)>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = SetLineRegex().Match(line);
            if (!match.Success) continue;

            var isCommented = !string.IsNullOrEmpty(match.Groups[1].Value);
            var varName = match.Groups[2].Value;
            var value = match.Groups[3].Value;

            if (!IsRotationVariable(varName)) continue;

            // Check if this is a continuation variable (e.g., sv_maprotation_1, _2)
            var (baseVar, suffixIndex) = ParseVariableSuffix(varName);
            if (IsContinuationVariable(baseVar, suffixIndex, varName))
            {
                if (!continuationParts.ContainsKey(baseVar))
                    continuationParts[baseVar] = [];
                continuationParts[baseVar].Add((suffixIndex, value, !isCommented));
                continue;
            }

            // Extract metadata from preceding comments
            var (title, author, dateText, rawComment) = ExtractMetadata(lines, i);

            // Parse the rotation value
            var (gameMode, mapNames) = ParseRotationValue(varName, value);

            if (string.IsNullOrEmpty(title))
            {
                title = $"{varName} rotation";
            }

            rotations.Add(new ParsedRotation(
                title, gameMode, mapNames, varName,
                IsActive: !isCommented, author, dateText, rawComment));
        }

        // Merge continuation parts into their parent rotations
        foreach (var rotation in rotations)
        {
            if (continuationParts.TryGetValue(rotation.ConfigVariableName, out var parts))
            {
                foreach (var part in parts.OrderBy(p => p.Index))
                {
                    var (_, additionalMaps) = ParseRotationValue(rotation.ConfigVariableName, part.Value);
                    rotation.MapNames.AddRange(additionalMaps);
                }
            }
        }

        return rotations;
    }

    private static bool IsRotationVariable(string varName)
    {
        return rotationVariablePrefixes.Any(prefix =>
            varName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            varName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsContinuationVariable(string baseVar, int suffixIndex, string fullVarName)
    {
        // sv_maprotation_1, sv_maprotation_2 are continuations of sv_maprotation
        // But scr_aacp_maps_1 is NOT a continuation — it's the base variable
        if (baseVar.Equals("sv_maprotation", StringComparison.OrdinalIgnoreCase) && suffixIndex > 0)
            return true;
        if (baseVar.Equals("scr_small_rotation", StringComparison.OrdinalIgnoreCase) && suffixIndex > 0)
            return true;
        if (baseVar.Equals("scr_med_rotation", StringComparison.OrdinalIgnoreCase) && suffixIndex > 0)
            return true;
        if (baseVar.Equals("scr_large_rotation", StringComparison.OrdinalIgnoreCase) && suffixIndex > 0)
            return true;

        return false;
    }

    private static (string BaseVar, int SuffixIndex) ParseVariableSuffix(string varName)
    {
        var lastUnderscore = varName.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < varName.Length - 1)
        {
            var suffix = varName[(lastUnderscore + 1)..];
            if (int.TryParse(suffix, out var index))
            {
                return (varName[..lastUnderscore], index);
            }
        }
        return (varName, 0);
    }

    private static (string GameMode, List<string> MapNames) ParseRotationValue(string varName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ("", []);

        // AACP format: semicolon-separated map names
        if (varName.StartsWith("scr_aacp_maps_", StringComparison.OrdinalIgnoreCase))
        {
            var maps = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();
            return ("", maps);
        }

        // Standard format: gametype {mode} map {map1} map {map2} ...
        var gameMode = "";
        var mapNames = new List<string>();
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var j = 0; j < tokens.Length; j++)
        {
            if (tokens[j].Equals("gametype", StringComparison.OrdinalIgnoreCase) && j + 1 < tokens.Length)
            {
                gameMode = tokens[j + 1];
                j++;
            }
            else if (tokens[j].Equals("map", StringComparison.OrdinalIgnoreCase) && j + 1 < tokens.Length)
            {
                mapNames.Add(tokens[j + 1]);
                j++;
            }
        }

        return (gameMode, mapNames);
    }

    private static (string Title, string? Author, string? DateText, string? RawComment) ExtractMetadata(string[] lines, int rotationLineIndex)
    {
        // Look at up to 3 preceding lines for metadata comments
        var title = "";
        string? author = null;
        string? dateText = null;
        string? rawComment = null;

        for (var offset = 1; offset <= 3 && rotationLineIndex - offset >= 0; offset++)
        {
            var commentLine = lines[rotationLineIndex - offset].Trim();
            if (!commentLine.StartsWith("//")) break;
            if (commentLine.StartsWith("//***")) break; // Section divider

            var commentText = commentLine.TrimStart('/').Trim();
            if (string.IsNullOrEmpty(commentText)) continue;

            rawComment = commentText;

            // Try to extract rotation name: "Rotation #17" or "Rotation 6"
            var rotationMatch = RotationNameRegex().Match(commentText);
            if (rotationMatch.Success && string.IsNullOrEmpty(title))
            {
                title = rotationMatch.Groups[1].Value.Trim();
            }

            // Try to extract author: "by Pengy" or "pengy" at end
            var authorMatch = AuthorRegex().Match(commentText);
            if (authorMatch.Success && author == null)
            {
                author = authorMatch.Groups[1].Value;
            }

            // Try to extract date: various formats
            var dateMatch = DateRegex().Match(commentText);
            if (dateMatch.Success && dateText == null)
            {
                dateText = dateMatch.Groups[1].Value;
            }

            // If we found a rotation identifier, use the full comment as title context
            if (rotationMatch.Success && string.IsNullOrEmpty(title))
            {
                title = commentText;
            }
        }

        return (title, author, dateText, rawComment);
    }
}
