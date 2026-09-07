using Microsoft.Playwright;
using System.Runtime.ExceptionServices;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

internal sealed class DiagnosticResourceCleanup(TestDiagnosticScope diagnostics)
{
    private readonly List<Exception> failures = [];

    internal bool HasFailures => failures.Count != 0;

    internal async Task RunAsync(string operation, Func<Task> action, Func<PlaywrightException, bool>? isExpectedDisconnect = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (PlaywrightException exception) when (isExpectedDisconnect?.Invoke(exception) == true)
        {
            diagnostics.RecordError($"{operation} (browser disconnected)", exception);
        }
        catch (Exception exception)
        {
            // Collect, rather than suppress, so every resource gets a cleanup attempt.
            failures.Add(exception);
            diagnostics.RecordError(operation, exception);
        }
    }

    internal void Run(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            diagnostics.RecordError(operation, exception);
        }
    }

    internal void AttachToPrimaryException(Exception primary)
    {
        if (HasFailures)
        {
            primary.Data["PortalTestCleanupErrors"] = failures.Select(exception => exception.ToString()).ToArray();
        }
    }

    internal void ThrowIfFailed()
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException("Portal test resource cleanup failed.", failures);
        }
    }
}
