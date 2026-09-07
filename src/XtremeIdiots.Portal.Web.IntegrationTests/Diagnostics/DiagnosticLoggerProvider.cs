using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

internal sealed class DiagnosticLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName)
    {
        return new DiagnosticLogger(this, categoryName);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        scopes = scopeProvider;
    }
    public void Dispose() { }

    private sealed class DiagnosticLogger(DiagnosticLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return provider.scopes.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None && TestDiagnosticScope.Current is not null;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = TestDiagnosticScope.Current;
            if (scope is null || !IsEnabled(logLevel))
            {
                return;
            }

            scope.Try("Formatting application log", () =>
            {
                var context = new List<string>();
                provider.scopes.ForEachScope((value, values) => values.Add(value?.ToString() ?? string.Empty), context);
                scope.Log($"{DateTimeOffset.UtcNow:O} [{logLevel}] {category}[{eventId.Id}] {formatter(state, exception)}" +
                    (context.Count == 0 ? string.Empty : $" | scopes: {string.Join(" => ", context)}") +
                    (exception is null ? string.Empty : $"{Environment.NewLine}{exception}"));
            });
        }
    }
}

internal sealed class DiagnosticRequestStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use(async (context, continuation) =>
            {
                // Kestrel requests do not inherit the test's ExecutionContext.
                var scope = TestDiagnosticScope.Find(context.Request.Headers[TestDiagnosticScope.HeaderName].ToString())
                    ?? TestDiagnosticScope.Current;
                using var activation = TestDiagnosticScope.Activate(scope);
                scope?.Log($"{DateTimeOffset.UtcNow:O} Request {context.Request.Method} {context.Request.Path}");
                try
                {
                    await continuation().ConfigureAwait(false);
                }
                finally
                {
                    scope?.Log($"{DateTimeOffset.UtcNow:O} Response {context.Response.StatusCode} {context.Request.Method} {context.Request.Path}");
                }
            });
            next(application);
        };
    }
}
