using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
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
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.ApiControllers;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.ApiControllers;

public sealed class ActivityLogControllerTests
{
    [Fact]
    public async Task Ajax_PassesActorSearchAndPagingToService()
    {
        var service = new Mock<IActivityLogService>();
        service.Setup(s => s.QueryEventsAsync(TimeSpan.FromDays(7), It.IsAny<IReadOnlyList<ActivityLogCategory>>(),
            It.IsAny<IReadOnlyList<string>>(), "map", 10, 5, "actorname", "asc", "123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityLogQueryResult { TotalCount = 100, FilteredCount = 6 });
        using var telemetry = new TelemetryConfiguration();
        var controller = CreateController(service.Object, Mock.Of<IRepositoryApiClient>(), telemetry);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
                {
                    draw = 4, start = 10, length = 5, search = new { value = "map" },
                    columns = new[] { new { name = "actorname" } }, order = new[] { new { column = 0, dir = "asc" } }
                }))) }
            }
        };

        var result = await controller.GetActivityLogAjax("7d", null, null, "123");

        var payload = JObject.FromObject(Assert.IsType<OkObjectResult>(result).Value!);
        Assert.Equal(4, payload["Draw"]!.Value<int>());
        Assert.Equal(100, payload["recordsTotal"]!.Value<int>());
        Assert.Equal(6, payload["recordsFiltered"]!.Value<int>());
        service.VerifyAll();
    }

    [Fact]
    public async Task UserSearch_UsesAllProfilesAndOnlyReturnsAuditActorIds()
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        var withId = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new { DisplayName = "Member", XtremeIdiotsForumId = "123" }))!;
        var withoutId = JsonConvert.DeserializeObject<UserProfileDto>(JsonConvert.SerializeObject(new { DisplayName = "Unlinked" }))!;
        repository.Setup(r => r.UserProfiles.V1.GetUserProfiles("Member", null, 0, 15, UserProfilesOrder.DisplayNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<UserProfileDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<UserProfileDto>>(new CollectionModel<UserProfileDto>([withId, withoutId]))));
        using var telemetry = new TelemetryConfiguration();
        var controller = CreateController(Mock.Of<IActivityLogService>(), repository.Object, telemetry);

        var result = await controller.GetActivityLogUsers(" Member ");

        var payload = JArray.FromObject(Assert.IsType<OkObjectResult>(result).Value!);
        Assert.Single(payload);
        Assert.Equal("123", payload[0]["id"]!.Value<string>());
        Assert.Equal("Member", payload[0]["text"]!.Value<string>());
        repository.VerifyAll();
    }

    [Fact]
    public async Task UserSearch_ReturnsUnavailableWhenRepositoryFails()
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(r => r.UserProfiles.V1.GetUserProfiles("Member", null, 0, 15, UserProfilesOrder.DisplayNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<UserProfileDto>>(HttpStatusCode.ServiceUnavailable, null));
        using var telemetry = new TelemetryConfiguration();
        var controller = CreateController(Mock.Of<IActivityLogService>(), repository.Object, telemetry);

        var result = await controller.GetActivityLogUsers("Member");

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    private static ActivityLogController CreateController(IActivityLogService service, IRepositoryApiClient repository, TelemetryConfiguration telemetry) => new(service, repository, new TelemetryClient(telemetry), NullLogger<ActivityLogController>.Instance,
            new ConfigurationBuilder().Build(), Mock.Of<IAuditLogger>());
}
