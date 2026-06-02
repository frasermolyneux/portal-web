using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Tests.Extensions;

public class FileTransportCompatibilityExtensionsTests
{
    private enum ExternalFileTransportType
    {
        Unknown = 0,
        Ftp = 1,
        Sftp = 2
    }

    private sealed class CompatibilitySource
    {
        public bool? FileTransportEnabled { get; set; }
        public ExternalFileTransportType? FileTransportType { get; set; }
    }

    [Fact]
    public void GetFileTransportType_WhenTypeMissingAndLegacyFtpEnabled_InfersFtp()
    {
        var source = new CompatibilitySource
        {
            FileTransportEnabled = null,
            FileTransportType = null
        };

        var result = source.GetFileTransportType(fileTransportEnabled: false, fallbackFtpEnabled: true);

        Assert.Equal(FileTransportType.Ftp, result);
    }

    [Fact]
    public void GetFileTransportType_WhenTypeMissingAndFileTransportEnabledWithoutLegacyFtp_InfersSftp()
    {
        var source = new CompatibilitySource
        {
            FileTransportEnabled = true,
            FileTransportType = null
        };

        var result = source.GetFileTransportType(fileTransportEnabled: true, fallbackFtpEnabled: false);

        Assert.Equal(FileTransportType.Sftp, result);
    }

    [Fact]
    public void GetFileTransportType_WhenTypeMissingAndBothFlagsFalse_InfersUnknown()
    {
        var source = new CompatibilitySource
        {
            FileTransportEnabled = false,
            FileTransportType = null
        };

        var result = source.GetFileTransportType(fileTransportEnabled: false, fallbackFtpEnabled: false);

        Assert.Equal(FileTransportType.Unknown, result);
    }

    [Fact]
    public void GetFileTransportType_WhenTypeProvided_UsesProvidedType()
    {
        var source = new CompatibilitySource
        {
            FileTransportEnabled = true,
            FileTransportType = ExternalFileTransportType.Sftp
        };

        var result = source.GetFileTransportType(fileTransportEnabled: true, fallbackFtpEnabled: true);

        Assert.Equal(FileTransportType.Sftp, result);
    }
}
