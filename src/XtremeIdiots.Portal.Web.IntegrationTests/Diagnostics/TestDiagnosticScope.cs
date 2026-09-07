using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Text.Json;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

internal sealed class TestDiagnosticScope
{
    internal const string HeaderName = "X-Portal-Test-Diagnostics";
    private readonly static AsyncLocal<TestDiagnosticScope?> ambient = new();
    private readonly static AsyncLocal<Guid?> currentCollection = new();
    private readonly static ConcurrentDictionary<string, TestDiagnosticScope> activeScopes = new();
    private readonly static ConcurrentDictionary<Guid, ConcurrentQueue<string>> initializationFailures = new();
    private readonly static JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly BoundedDiagnosticBuffer applicationLogs = new();
    private readonly BoundedDiagnosticBuffer errors = new();
    private readonly ConcurrentDictionary<string, byte> importedScopes = new();
    private readonly HashSet<string> relatedArtifacts = [];
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly Guid? collectionId = currentCollection.Value;
    private int browserNumber;
    private int completed;

    internal TestDiagnosticScope(string displayName, string className, string methodName, string? root = null)
    {
        DisplayName = displayName;
        ClassName = className;
        MethodName = methodName;
        Id = Guid.NewGuid().ToString("N");
        root ??= Environment.GetEnvironmentVariable("PORTAL_TEST_DIAGNOSTICS_DIRECTORY");
        root = string.IsNullOrWhiteSpace(root) ? Path.Combine(AppContext.BaseDirectory, "TestResults", "diagnostics") : root;
        DirectoryPath = Path.Combine(root, $"{SafeName(methodName)}-{Id}");
        activeScopes[Id] = this;
    }

    internal static TestDiagnosticScope? Current => ambient.Value;
    internal string Id { get; }
    internal string DisplayName { get; }
    internal string ClassName { get; }
    internal string MethodName { get; }
    internal string DirectoryPath { get; }
    internal bool HasErrors => errors.Count > 0;

    internal static TestDiagnosticScope? Find(string id)
    {
        return activeScopes.GetValueOrDefault(id);
    }

    internal static IDisposable Activate(TestDiagnosticScope? scope)
    {
        var previous = ambient.Value;
        ambient.Value = scope;
        return new RestoreScope(previous);
    }

    internal static IDisposable ActivateCollection(Guid collection)
    {
        var previous = currentCollection.Value;
        currentCollection.Value = collection;
        return new RestoreCollection(previous, collection);
    }

    internal void IncludeInitializationFailures(Guid collection)
    {
        if (initializationFailures.TryGetValue(collection, out var directories))
        {
            foreach (var directory in directories)
            {
                relatedArtifacts.Add(directory);
            }
        }
    }

    internal void ImportApplicationLogs(TestDiagnosticScope source)
    {
        if (importedScopes.TryAdd(source.Id, 0))
        {
            Log($"Shared setup logs: {source.DisplayName}");
            foreach (var line in source.applicationLogs.Snapshot().Split(Environment.NewLine))
            {
                Log(line);
            }
        }
    }

    internal string NextBrowserDirectory(string name)
    {
        return Path.Combine(DirectoryPath, $"browser-{Interlocked.Increment(ref browserNumber):D3}-{SafeName(name)}");
    }

    internal void Log(string message)
    {
        if (Volatile.Read(ref completed) == 0)
        {
            applicationLogs.Add(message);
        }
    }

    internal void RecordError(string operation, Exception exception)
    {
        var message = $"{DateTimeOffset.UtcNow:O} {operation}: {exception}";
        errors.Add(message);
        Console.Error.WriteLine($"Portal test diagnostics [{DisplayName}]: {message}");
    }

    internal async Task TryAsync(string operation, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedCaptureError(exception))
        {
            RecordError(operation, exception);
        }
    }

    internal void Try(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (IsExpectedCaptureError(exception))
        {
            RecordError(operation, exception);
        }
    }

    internal void WriteText(string path, string content)
    {
        Try($"Writing {path}", () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        });
    }

    private static bool IsExpectedCaptureError(Exception exception)
    {
        return exception is PlaywrightException or IOException or UnauthorizedAccessException or ObjectDisposedException;
    }

    internal void SaveFailure(string outcome, object? failure = null)
    {
        WriteText(Path.Combine(DirectoryPath, "application.log"), applicationLogs.Snapshot());
        WriteText(Path.Combine(DirectoryPath, "metadata.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            id = Id,
            displayName = DisplayName,
            className = ClassName,
            methodName = MethodName,
            startedAt,
            finishedAt = DateTimeOffset.UtcNow,
            outcome,
            failure,
            browserContexts = browserNumber,
            applicationLogLimit = BoundedDiagnosticBuffer.MaxEntries,
            relatedArtifactDirectories = relatedArtifacts,
        }, jsonOptions));
        WriteText(Path.Combine(DirectoryPath, "diagnostics-errors.log"), errors.Snapshot());
    }

    internal void Complete(string outcome, object? failure = null)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        if (outcome is "Passed" or "Skipped")
        {
            Try("Removing successful test diagnostics", () =>
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            });
        }
        else
        {
            SaveFailure(outcome, failure);
            if (outcome == "InitializationFailed" && collectionId is Guid collection)
            {
                initializationFailures.GetOrAdd(collection, _ => new ConcurrentQueue<string>()).Enqueue(DirectoryPath);
            }
        }

        activeScopes.TryRemove(Id, out _);
    }

    internal string ArtifactOutput
        => $"{Environment.NewLine}Portal test diagnostics: {DirectoryPath}{Environment.NewLine}" +
        string.Concat(relatedArtifacts.Select(path => $"Related setup diagnostics: {path}{Environment.NewLine}")) +
        (HasErrors ? $"Diagnostics capture/cleanup errors:{Environment.NewLine}{errors.Snapshot()}" : string.Empty);

    private static string SafeName(string value)
    {
        var safe = new string([.. value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_').Take(64)]);
        return string.IsNullOrEmpty(safe) ? "test" : safe;
    }

    private sealed class RestoreScope(TestDiagnosticScope? previous) : IDisposable
    {
        public void Dispose()
        {
            ambient.Value = previous;
        }
    }

    private sealed class RestoreCollection(Guid? previous, Guid collection) : IDisposable
    {
        public void Dispose()
        {
            initializationFailures.TryRemove(collection, out _);
            currentCollection.Value = previous;
        }
    }
}

internal sealed class BoundedDiagnosticBuffer
{
    internal const int MaxEntries = 2048;
    private const int MaxEntryLength = 8192;
    private readonly Queue<string> entries = new();
    private readonly Lock gate = new();
    private int dropped;

    internal int Count {
        get {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    internal void Add(string message)
    {
        lock (gate)
        {
            if (entries.Count == MaxEntries)
            {
                entries.Dequeue();
                dropped++;
            }

            entries.Enqueue(message.Length > MaxEntryLength ? message[..MaxEntryLength] + " [truncated]" : message);
        }
    }

    internal string Snapshot()
    {
        lock (gate)
        {
            var prefix = dropped == 0 ? string.Empty : $"[{dropped} older entries discarded]{Environment.NewLine}";
            return prefix + string.Join(Environment.NewLine, entries);
        }
    }
}
