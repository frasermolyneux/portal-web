using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public class SyncApiClientTests
{
    [Fact]
    public async Task ApplyAndPush_UseAuthenticatedRoutesIncludingServerGameType()
    {
        var serverId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var requests = new List<string>();
        using var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
            requests.Add(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        Assert.True((await client.TriggerApply(assignmentId)).Success);
        Assert.True((await client.TriggerPushMap(serverId, GameType.CallOfDuty4x, "mp_test")).Success);

        Assert.Equal(new[]
        {
            $"/api/map-rotations/apply/{assignmentId}",
            $"/api/maps/push/{serverId}/CallOfDuty4x/mp_test"
        }, requests);
    }

    [Fact]
    public async Task Terminate_AcceptedCooperativeCancellation_IsNotCompletedSuccess()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(/*lang=json,strict*/ """{"terminated":false,"cancellationRequested":true}""")
        });
        using var httpClient = new HttpClient(handler);

        var result = await CreateClient(httpClient).TerminateOrchestration("maprot-apply-synthetic");

        Assert.False(result.Success);
        Assert.True(result.CancellationRequested);
    }

    [Fact]
    public async Task Terminate_LegacyHardCancellationConflict_IsSurfacedWithoutSuccess()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(/*lang=json,strict*/ """{"error":"Legacy workflow cancellation is unsafe; service-side investigation required."}""")
        });
        using var httpClient = new HttpClient(handler);

        var result = await CreateClient(httpClient).TerminateOrchestration("maprot-sync-synthetic");

        Assert.False(result.Success);
        Assert.False(result.CancellationRequested);
        Assert.Contains("service-side investigation", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_CompletedBusinessFailure_PreservesFalseOutcomeAndProgressError()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(/*lang=json,strict*/ """{"instanceId":"synthetic","runtimeStatus":"Completed","success":false,"result":{"success":false,"error":"linked target failed"},"progress":{"operation":"Apply","totalMaps":1,"completedMaps":1,"maps":[{"mapName":"configuration","status":"Failed","error":"linked target failed"}]}}""")
        });
        using var httpClient = new HttpClient(handler);

        var status = await CreateClient(httpClient).GetOrchestrationStatus("synthetic");

        Assert.Equal(OrchestrationStatusQueryOutcome.Found, status.Outcome);
        Assert.False(status.Result!.Success);
        Assert.Equal("linked target failed", Assert.Single(status.Result.Progress!.Maps).Error);
    }

    [Fact]
    public async Task Apply_ErrorResponse_IsNotSuccess()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var httpClient = new HttpClient(handler);
        var result = await CreateClient(httpClient).TriggerApply(Guid.NewGuid());
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }

    private static SyncApiClient CreateClient(HttpClient httpClient)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SyncApi:BaseUrl"] = "https://sync.test.invalid",
            ["SyncApi:ApplicationAudience"] = "api://synthetic"
        }).Build();
        return new SyncApiClient(httpClient, configuration, NullLogger<SyncApiClient>.Instance, new SyntheticCredential());
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class SyntheticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new("synthetic-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}
