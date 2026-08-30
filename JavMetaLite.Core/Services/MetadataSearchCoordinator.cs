using System.Diagnostics;
using System.Runtime.ExceptionServices;
using JavMetaLite.Core.Models;

namespace JavMetaLite.Core.Services;

public static class MetadataSearchCoordinator
{
    private static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(10);

    public static async Task<MetadataSourceSearchAttempt> SearchSingleAsync(
        string rawId,
        IMetadataProvider provider,
        CancellationToken cancellationToken = default,
        TimeSpan? providerTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var id = MovieIdParser.Normalize(rawId);
        var attempt = await RunProviderAsync(
            id,
            provider,
            "single",
            cancellationToken,
            ValidateTimeout(providerTimeout));
        if (attempt.Success)
        {
            return attempt;
        }

        ExceptionDispatchInfo.Capture(attempt.Error!).Throw();
        throw new InvalidOperationException("来源搜索没有返回结果。 ");
    }

    public static async Task<MultiSourceSearchResult> SearchAllAsync(
        string rawId,
        IMetadataProvider primaryProvider,
        IMetadataProvider secondaryProvider,
        CancellationToken cancellationToken = default,
        TimeSpan? providerTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(primaryProvider);
        ArgumentNullException.ThrowIfNull(secondaryProvider);

        var id = MovieIdParser.Normalize(rawId);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("请先输入影片番号。", nameof(rawId));
        }

        if (ReferenceEquals(primaryProvider, secondaryProvider) ||
            string.Equals(primaryProvider.Name, secondaryProvider.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("多来源搜索需要两个不同的资料来源。 ", nameof(secondaryProvider));
        }

        var timeout = ValidateTimeout(providerTimeout);
        var attempts = await Task.WhenAll(
            RunProviderAsync(id, primaryProvider, "multi", cancellationToken, timeout),
            RunProviderAsync(id, secondaryProvider, "multi", cancellationToken, timeout));
        var successfulMetadata = attempts
            .Where(attempt => attempt.Metadata is not null)
            .Select(attempt => attempt.Metadata!)
            .ToArray();

        if (successfulMetadata.Length == 0)
        {
            throw new MultiSourceSearchException(id, attempts);
        }

        if (successfulMetadata.Length == 1)
        {
            return new MultiSourceSearchResult(successfulMetadata[0], successfulMetadata, attempts);
        }

        try
        {
            var merged = MetadataMerger.Merge(successfulMetadata[0], successfulMetadata[1]);
            return new MultiSourceSearchResult(merged, successfulMetadata, attempts);
        }
        catch (InvalidDataException exception)
        {
            AppLog.Error(
                $"多来源合并被拒绝 id={id} primary={primaryProvider.Name} secondary={secondaryProvider.Name}",
                exception);
            throw;
        }
    }

    private static async Task<MetadataSourceSearchAttempt> RunProviderAsync(
        string id,
        IMetadataProvider provider,
        string mode,
        CancellationToken cancellationToken,
        TimeSpan providerTimeout)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(providerTimeout);
        try
        {
            var metadata = await provider.SearchAsync(id, timeoutSource.Token);
            stopwatch.Stop();
            var fieldCount = CountCandidateFields(metadata);
            AppLog.Info(
                $"来源搜索成功 mode={mode} source={provider.Name} id={id} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds} fields={fieldCount} screenshots={metadata.ScreenshotUrls.Count}");
            return new MetadataSourceSearchAttempt(
                provider.Name,
                provider.DisplayName,
                stopwatch.Elapsed,
                metadata,
                null,
                fieldCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested)
        {
            stopwatch.Stop();
            var timeoutException = new MetadataSourceTimeoutException(
                provider.Name,
                provider.DisplayName,
                providerTimeout,
                exception);
            AppLog.Warning(
                $"来源搜索超时 mode={mode} source={provider.Name} id={id} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds} timeoutMs={providerTimeout.TotalMilliseconds:0}",
                timeoutException);
            return new MetadataSourceSearchAttempt(
                provider.Name,
                provider.DisplayName,
                stopwatch.Elapsed,
                null,
                timeoutException,
                0);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            AppLog.Warning(
                $"来源搜索失败 mode={mode} source={provider.Name} id={id} " +
                $"elapsedMs={stopwatch.ElapsedMilliseconds} error={exception.GetType().Name}",
                exception);
            return new MetadataSourceSearchAttempt(
                provider.Name,
                provider.DisplayName,
                stopwatch.Elapsed,
                null,
                exception,
                0);
        }
    }

    private static int CountCandidateFields(MovieMetadata metadata) =>
        MetadataSourceSnapshot.FromMetadata(metadata).Values.Values.Count(value => !string.IsNullOrWhiteSpace(value));

    private static TimeSpan ValidateTimeout(TimeSpan? providerTimeout)
    {
        var timeout = providerTimeout ?? DefaultProviderTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(providerTimeout), "资料来源超时必须大于零。 ");
        }

        return timeout;
    }
}
