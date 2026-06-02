using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class GameServerFileTransportExtensionsTests
{
    [Fact]
    public void SetFileTransportProperties_WhenEnabledAndUnknown_DefaultsToFtp()
    {
        var dto = new CreateGameServerDto("Server", GameType.CallOfDuty4, "host", 28960);

        dto.SetFileTransportProperties(true, FileTransportType.Unknown);

        Assert.True(dto.FileTransportEnabled);
        Assert.Equal(FileTransportType.Ftp, dto.FileTransportType);
    }

    [Fact]
    public void SetFileTransportProperties_WhenDisabled_UsesUnknown()
    {
        var dto = new EditGameServerDto(Guid.NewGuid());

        dto.SetFileTransportProperties(false, FileTransportType.Sftp);

        Assert.False(dto.FileTransportEnabled);
        Assert.Equal(FileTransportType.Unknown, dto.FileTransportType);
    }
}
