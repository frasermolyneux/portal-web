using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class NamespaceSettingsParserTests
{
    private readonly NamespaceSettingsParser parser = new();
    private readonly ILogger logger = Mock.Of<ILogger>();

    [Fact]
    public void PopulateGameServerDetails_ActiveTransportAndRcon_MapsViewDataValues()
    {
        var viewData = new Dictionary<string, object?>();

        var sftpConfig = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "hostname": "sftp.example.com",
          "port": 22,
          "username": "ops-user",
          "password": "sftp-secret"
        }
        """);

        var rconConfig = BuildConfiguration("rcon", /*lang=json,strict*/ """
        {
          "password": "rcon-secret"
        }
        """);

        parser.PopulateGameServerDetails(viewData, FileTransportType.Sftp, sftpConfig, logger);
        parser.PopulateGameServerDetails(viewData, FileTransportType.Sftp, rconConfig, logger);

        Assert.Equal("sftp.example.com", viewData["FtpHostname"]);
        Assert.Equal(22, viewData["FtpPort"]);
        Assert.Equal("ops-user", viewData["FtpUsername"]);
        Assert.Equal("sftp-secret", viewData["FtpPassword"]);
        Assert.Equal(FileTransportType.Sftp, viewData["FileTransportType"]);
        Assert.Equal("rcon-secret", viewData["RconPassword"]);
    }

    [Fact]
    public void PopulateExistingCredentials_BlankCredentialFields_ArePreservedFromCurrentNamespaces()
    {
        var model = new GameServerEditViewModel
        {
            FileTransportConfigPassword = null,
            FileTransportConfigHostKeyFingerprint = null,
            RconConfigPassword = null
        };

        var sftpConfig = BuildConfiguration("sftp", /*lang=json,strict*/ """
        {
          "password": "existing-sftp-password",
          "hostKeyFingerprint": "aa:bb:cc"
        }
        """);

        var rconConfig = BuildConfiguration("rcon", /*lang=json,strict*/ """
        {
          "password": "existing-rcon-password"
        }
        """);

        parser.PopulateExistingCredentials(
            model,
            activeTransportNamespace: "sftp",
            sftpConfig,
            needsFileTransportPassword: true,
            needsFileTransportHostKeyFingerprint: true,
            needsRconPassword: true,
            logger);

        parser.PopulateExistingCredentials(
            model,
            activeTransportNamespace: "sftp",
            rconConfig,
            needsFileTransportPassword: true,
            needsFileTransportHostKeyFingerprint: true,
            needsRconPassword: true,
            logger);

        Assert.Equal("existing-sftp-password", model.FileTransportConfigPassword);
        Assert.Equal("aa:bb:cc", model.FileTransportConfigHostKeyFingerprint);
        Assert.Equal("existing-rcon-password", model.RconConfigPassword);
    }

    private static ConfigurationDto BuildConfiguration(string ns, string configuration)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = ns,
            Configuration = configuration
        }))!;
    }
}
