using Microsoft.Extensions.Logging;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public sealed class GameServerConfigurationReadServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reads_DeduplicateServersAndProjectOnlyRequestedCredentials(bool includeCredentials)
    {
        var serverId = Guid.NewGuid();
        var client = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        client.Setup(x => x.GameServerConfigurations.V1.GetConfigurations(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(
                Configuration("ftp", /*lang=json,strict*/ """{"hostname":"ftp.example.invalid","username":"fixture-user","password":"fixture-password","port":2121}"""),
                Configuration("sftp", /*lang=json,strict*/ """{"hostname":"sftp.example.invalid","username":"fixture-user","password":"fixture-password"}"""),
                Configuration("rcon", /*lang=json,strict*/ """{"password":"fixture-rcon"}"""),
                Configuration("serverlist", /*lang=json,strict*/ """{"htmlBanner":"fixture-banner"}""")));
        using var coordinator = new DependencyReadCoordinator();
        var service = new GameServerConfigurationReadService(client.Object, new NamespaceSettingsParser(), coordinator,
            Mock.Of<ILogger<GameServerConfigurationReadService>>());

        var result = await service.GetAsync([serverId, serverId], includeCredentials);

        var settings = Assert.Single(result).Value;
        Assert.Equal("ftp.example.invalid", settings.Ftp.Hostname);
        Assert.Equal(2121, settings.Ftp.Port);
        Assert.Equal(22, settings.Sftp.Port);
        Assert.Equal("fixture-banner", settings.HtmlBanner);
        Assert.Equal(includeCredentials ? "fixture-rcon" : null, settings.RconPassword);
        Assert.Equal(includeCredentials ? "fixture-password" : null, settings.Ftp.Password);
        Assert.Equal(includeCredentials ? "fixture-user" : null, settings.Sftp.Username);
        client.Verify(x => x.GameServerConfigurations.V1.GetConfigurations(serverId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reads_DoNotRetainConfigurationAfterAnUpdate()
    {
        var serverId = Guid.NewGuid();
        var client = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        client.SetupSequence(x => x.GameServerConfigurations.V1.GetConfigurations(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(Configuration("serverlist", /*lang=json,strict*/ """{"htmlBanner":"before"}""")))
            .ReturnsAsync(Response(Configuration("serverlist", /*lang=json,strict*/ """{"htmlBanner":"after"}""")));
        using var coordinator = new DependencyReadCoordinator();
        var service = new GameServerConfigurationReadService(client.Object, new NamespaceSettingsParser(), coordinator,
            Mock.Of<ILogger<GameServerConfigurationReadService>>());

        Assert.Equal("before", (await service.GetAsync([serverId]))[serverId].HtmlBanner);
        Assert.Equal("after", (await service.GetAsync([serverId]))[serverId].HtmlBanner);
    }

    private static ConfigurationDto Configuration(string ns, string value)
    {
        return JsonConvert.DeserializeObject<ConfigurationDto>(JsonConvert.SerializeObject(new { Namespace = ns, Configuration = value }))!;
    }

    private static ApiResult<CollectionModel<ConfigurationDto>> Response(params ConfigurationDto[] configurations)
    {
        return new(HttpStatusCode.OK, new ApiResponse<CollectionModel<ConfigurationDto>>(new CollectionModel<ConfigurationDto>(configurations)));
    }
}
