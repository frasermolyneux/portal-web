using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Credentials;

/// <summary>
/// Shapes the repository mock so the Credentials index page renders a single Call of Duty 4 server
/// with both RCON and SFTP configuration present. Combined with the various role/grant principals
/// this exercises the per-server, per-credential-type content filtering performed by the view.
/// </summary>
internal sealed class CredentialsContentScenario
{
    public const string RconPassword = "RconSecret123";
    public const string SftpHostname = "sftp.creds.example.com";
    public const string SftpUsername = "ops-user";
    public const string SftpPassword = "SftpSecret456";

    public CredentialsContentScenario()
    {
        GameServerId = Guid.Parse(TestPrincipalProfiles.CredentialServerId);
        GameServer = CreateGameServer(GameServerId);

        var rconConfiguration = CreateConfiguration("rcon", JsonConvert.SerializeObject(new
        {
            password = RconPassword,
        }));
        var sftpConfiguration = CreateConfiguration("sftp", JsonConvert.SerializeObject(new
        {
            hostname = SftpHostname,
            port = 22,
            username = SftpUsername,
            password = SftpPassword,
            mapsRootPath = "/maps",
        }));

        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };

        var gameServersResult = new ApiResult<CollectionModel<GameServerDto>>(
            HttpStatusCode.OK,
            new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([GameServer])));

        // Return the server whether the controller queries by game type breadth or by explicit
        // per-server credential grant; de-duplication in the controller collapses the two paths.
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), It.IsAny<GameServerFilter?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<GameServerOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameServersResult);

        Mock.Get(RepositoryClient.Object.GameServerConfigurations.V1)
            .Setup(api => api.GetConfigurations(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<ConfigurationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>([rconConfiguration, sftpConfiguration]))));
    }

    public GameServerDto GameServer { get; }

    public Guid GameServerId { get; }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }

    private static ConfigurationDto CreateConfiguration(string ns, string configuration)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new
        {
            Namespace = ns,
            Configuration = configuration,
            LastModifiedUtc = DateTime.UtcNow,
        }))!;
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "Credentials Coverage Server",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.9",
            QueryPort = 28965,
            AgentEnabled = false,
            FileTransportEnabled = true,
            FileTransportType = FileTransportType.Sftp.ToString(),
            FtpEnabled = true,
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1,
        }))!;
    }
}
