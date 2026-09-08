namespace XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;

[Trait("Category", "HttpIntegration")]
public sealed class RequestGateTests
{
    [Fact]
    public async Task HeldRequest_ReportsEntryAndWaitsForExplicitRelease()
    {
        var gate = new RequestGate(hold: true);
        var request = gate.WaitAsync();
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(request.IsCompleted);

        gate.Release();
        await request;
        gate.Release();
    }

    [Fact]
    public async Task ImmediateRequest_DoesNotNeedRelease()
    {
        var gate = new RequestGate();
        await gate.WaitAsync();
        Assert.True(gate.Entered.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HeldRequest_ObservesCancellation()
    {
        var gate = new RequestGate(hold: true);
        using var cancellation = new CancellationTokenSource();
        var request = gate.WaitAsync(cancellation.Token);
        await gate.Entered;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        gate.Release();
    }
}
