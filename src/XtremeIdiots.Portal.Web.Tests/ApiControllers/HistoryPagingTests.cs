using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ChatMessages;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public sealed class HistoryPagingTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task History_UsesCountFreeContractAndPreservesUpstreamFailures(bool events, bool success)
    {
        var client = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        var status = success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
        client.Setup(x => x.GameServersEvents.V1.GetGameServerEventsPage(
                null, null, "MapChange", 10, 10, GameServerEventOrder.TimestampDesc, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<HistoryPageDto<GameServerEventDto>>(status,
                new ApiResponse<HistoryPageDto<GameServerEventDto>>(new HistoryPageDto<GameServerEventDto>([], false))));
        client.Setup(x => x.ChatMessages.V1.GetChatMessagesPage(
                null, null, null, "signal", 10, 10, ChatMessageOrder.TimestampDesc, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<HistoryPageDto<ChatMessageDto>>(status,
                new ApiResponse<HistoryPageDto<ChatMessageDto>>(new HistoryPageDto<ChatMessageDto>([], false))));
        using var telemetry = new TelemetryConfiguration();
        var controller = new ServerAdminController(Mock.Of<IAuthorizationService>(), client.Object,
            new TelemetryClient(telemetry), NullLogger<ServerAdminController>.Instance,
            new ConfigurationBuilder().Build(), Mock.Of<IAuditLogger>());
        var body = JsonConvert.SerializeObject(new { draw = 3, start = 10, length = 10, search = new { value = "signal" } });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(body)) }
            }
        };

        var result = events
            ? await controller.GetServerEventsAjax(null, null, " MapChange ")
            : await controller.GetChatLogAjax(true);

        if (!success)
        {
            Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
            return;
        }

        var payload = JObject.FromObject(Assert.IsType<OkObjectResult>(result).Value!);
        Assert.Equal(10, payload["start"]!.Value<int>());
        Assert.False(payload["hasMore"]!.Value<bool>());
        Assert.Empty(Assert.IsType<JArray>(payload["data"]));
        Assert.Null(payload["recordsTotal"]);
        Assert.Null(payload["recordsFiltered"]);
    }
}
