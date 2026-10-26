using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Text;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Files;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

internal sealed class MapRotationImportScenario
{
    public const string ConfigPath = "/configs/maprotation.cfg";
    public Mock<IRepositoryApiClient> Repository { get; }
    public Mock<IServersApiClient> Servers { get; } = new(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
    public MapRotationImportScenario()
    {
        var assignment = new MapRotationAssignmentScenario(fileTransportEnabled: true);
        Repository = assignment.RepositoryClient;
        Repository.Setup(x => x.Maps.V1.GetMaps(It.IsAny<GameType?>(), It.IsAny<string[]?>(), It.IsAny<MapsFilter?>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MapsOrder?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([]))));
        Repository.Setup(x => x.MapRotations.V1.GetMapRotations(It.IsAny<GameType[]?>(), It.IsAny<string?>(), It.IsAny<MapRotationsFilter?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MapRotationsOrder?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationDto>>(new CollectionModel<MapRotationDto>([]))));

        var bytes = Encoding.UTF8.GetBytes("""
            set sv_mapRotationLoadBased "1"
            set sv_mapRotationScramble "1"
            // set sv_mapRotation "gametype gametype map map"
            set sv_mapRotation ""
            // set rcon_password "synthetic-secret"
            // Rotation #4 by syntheticAuthor 2026-10-26
            set sv_maprotation "gametype dm map mp_alpha map map mp_beta"
            """);
        Servers.Setup(x => x.Files.V1.GetMetadata(MapRotationAssignmentScenario.PermittedServerId,
            It.Is<GetEntryMetadataQueryDto>(q => q.Path == ConfigPath), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FileEntryMetadataDto>(HttpStatusCode.OK, new ApiResponse<FileEntryMetadataDto>(
                new FileEntryMetadataDto("maprotation.cfg", ConfigPath, FileEntryType.File, bytes.LongLength, null))));
        Servers.Setup(x => x.Files.V1.GetContent(MapRotationAssignmentScenario.PermittedServerId,
            It.Is<GetFileContentQueryDto>(q => q.Path == ConfigPath), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<FileContentDto>(HttpStatusCode.OK, new ApiResponse<FileContentDto>(
                new FileContentDto(ConfigPath, FileContentMode.Binary, "base64", bytes.LongLength, null, Convert.ToBase64String(bytes)))));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(Repository.Object);
        services.RemoveAll<IServersApiClient>();
        services.AddSingleton(Servers.Object);
    }
}
