using Microsoft.Extensions.Caching.Memory;
using MX.Api.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Helpers;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class MapExperienceService(
    IRepositoryApiClient repository, IMemoryCache cache, DependencyReadCoordinator coordinator)
{
    private readonly static Lock gamesGate = new();

    public Task<PublicServerRotationsDto> Rotations(CancellationToken cancellationToken)
    {
        return Cached("maps:public-rotations", async ct =>
            {
                var rotationsTask = repository.MapRotations.V1.GetPublicServerRotations(ct);
                var namesTask = PublicNames(ct);
                await Task.WhenAll(rotationsTask, namesTask).ConfigureAwait(false);
                var result = await rotationsTask.ConfigureAwait(false);
                if (!result.IsSuccess || result.Result?.Data is not { } data)
                    throw new InvalidOperationException("Public map rotations are unavailable.");
                var names = await namesTask.ConfigureAwait(false);
                return data with
                {
                    Servers = [.. data.Servers.Select(server =>
                    {
                        var title = CodColorHelper.PlainText(names?.GetValueOrDefault(server.GameServerId) ?? "");
                        return server with { Title = string.IsNullOrWhiteSpace(title) ? CodColorHelper.PlainText(server.Title) : title };
                    })]
                };
            }, cancellationToken);
    }

    private async Task<Dictionary<Guid, string?>?> PublicNames(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var result = await repository.LiveStatus.V1.GetAllGameServerLiveStatuses(timeout.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            return result is { IsSuccess: true }
                ? result.Result?.Data?.Items?.ToDictionary(status => status.ServerId, status => status.Title)
                : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<PublicServerRotationsDto> BrowsableRotations(CancellationToken cancellationToken)
    {
        var rotations = await Rotations(cancellationToken).ConfigureAwait(false);
        return rotations with
        {
            Servers = [.. rotations.Servers.Where(server => server.GameType != GameType.Unknown && server.Maps.Count > 0)]
        };
    }

    public Task<MapGamesDto> Games(CancellationToken cancellationToken)
    {
        const string key = "maps:catalogue-games";
        cancellationToken.ThrowIfCancellationRequested();
        Task<MapGamesDto> pending;
        lock (gamesGate)
        {
            // Share only in-flight reads; completed results never hide catalogue changes.
            if (cache.TryGetValue<Task<MapGamesDto>>(key, out var existing) && existing is { IsCompleted: false })
                pending = existing;
            else
            {
                pending = ReadGames();
                cache.Set(key, pending, TimeSpan.FromSeconds(15));
            }
        }

        return pending.WaitAsync(cancellationToken);
    }

    private async Task<MapGamesDto> ReadGames()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await coordinator.RunAsync(DependencyWorkload.MapMetadata, "maps:catalogue-games-read", async () =>
        {
            var result = await repository.Maps.V1.GetMapGames(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return result.IsSuccess && result.Result?.Data is { } data
                ? data : throw new InvalidOperationException("Map games are unavailable.");
        }, timeout.Token).ConfigureAwait(false);
    }

    public Task<ApiResult<PublicServerRotationDto>> DownloadPreview(PublicServerRotationDto server,
        Func<CancellationToken, Task<ApiResult<PublicServerRotationDto>>> read, CancellationToken cancellationToken)
    {
        var snapshot = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(server)));
        return Cached($"maps:download-preview:{server.GameServerId}:{snapshot}", read, cancellationToken,
            canCache: result => result.IsSuccess && result.Result?.Data is not null, workload: DependencyWorkload.MapDownloads);
    }

    public Task<MapInsightsDto> Insights(int days, GameType? family, Guid? server, CancellationToken cancellationToken)
    {
        var to = DateTime.UtcNow;
        return Cached($"maps:insights:{days}:{family}:{server}:{to:yyyyMMddHH}", async ct =>
        {
            var result = await repository.MapAnalytics.V1.GetVoteInsights(to.AddDays(-days), to, family, server, ct).ConfigureAwait(false);
            return result.IsSuccess && result.Result?.Data is { } data
                ? data : throw new InvalidOperationException("Map insights are unavailable.");
        }, cancellationToken, TimeSpan.FromMinutes(2));
    }

    private Task<T> Cached<T>(string key, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken,
        TimeSpan? lifetime = null, Func<T, bool>? canCache = null, DependencyWorkload workload = DependencyWorkload.MapMetadata) where T : class
    {
        return cache.TryGetValue<T>(key, out var cached) && cached is not null
            ? Task.FromResult(cached)
            : coordinator.RunAsync(workload, key, async () =>
            {
                if (cache.TryGetValue<T>(key, out var existing) && existing is not null)
                    return existing;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var result = await read(timeout.Token).ConfigureAwait(false);
                if (canCache?.Invoke(result) != false)
                    cache.Set(key, result, lifetime ?? TimeSpan.FromSeconds(30));
                return result;
            }, cancellationToken);
    }
}
