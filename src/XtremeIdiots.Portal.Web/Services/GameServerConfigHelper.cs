using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.Services;

public static class GameServerConfigHelper
{
    public static string GetConfigValue(
        IReadOnlyDictionary<Guid, GameServerReadSettings>? configs,
        Guid serverId, string ns, string property)
    {
        if (configs is null || !configs.TryGetValue(serverId, out var settings))
            return string.Empty;

        if (ns == "rcon" && property == "password")
            return settings.RconPassword ?? string.Empty;
        if (ns == "serverlist" && property == "htmlBanner")
            return settings.HtmlBanner ?? string.Empty;

        var transport = ns switch { "ftp" => settings.Ftp, "sftp" => settings.Sftp, _ => null };
        return property switch
        {
            "hostname" => transport?.Hostname ?? string.Empty,
            "username" => transport?.Username ?? string.Empty,
            "password" => transport?.Password ?? string.Empty,
            _ => string.Empty
        };
    }

    public static int GetConfigIntValue(
        IReadOnlyDictionary<Guid, GameServerReadSettings>? configs,
        Guid serverId, string ns, string property, int defaultValue = 0)
    {
        return configs is null || !configs.TryGetValue(serverId, out var settings) || property != "port"
            ? defaultValue
            : ns switch
            {
                "ftp" => settings.Ftp.Port,
                "sftp" => settings.Sftp.Port,
                _ => defaultValue
            };
    }
}
