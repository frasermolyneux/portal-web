using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class GameServerFileTransportExtensions
{
    public static void SetFileTransportProperties(this CreateGameServerDto dto, bool enabled, FileTransportType transportType)
    {
        dto.FileTransportEnabled = enabled;
        dto.FileTransportType = ResolveTransportType(enabled, transportType);
    }

    public static void SetFileTransportProperties(this EditGameServerDto dto, bool enabled, FileTransportType transportType)
    {
        dto.FileTransportEnabled = enabled;
        dto.FileTransportType = ResolveTransportType(enabled, transportType);
    }

    private static FileTransportType ResolveTransportType(bool enabled, FileTransportType transportType)
    {
        if (!enabled)
        {
            return FileTransportType.Unknown;
        }

        return transportType == FileTransportType.Unknown ? FileTransportType.Ftp : transportType;
    }
}
