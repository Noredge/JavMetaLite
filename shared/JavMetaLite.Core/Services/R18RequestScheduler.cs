using System.Net;
using System.Net.Http;

namespace JavMetaLite.Core.Services;

// One gate for every R18 API entry point, including fallback URLs and browser imports.
// Queue/cooldown time is deliberately outside the actual HTTP request timeout.
public sealed class R18RequestScheduler
{
    internal static R18RequestScheduler Shared { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minimumInterval;
    private readonly TimeSpan _maximumAutomaticWait;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private DateTimeOffset _nextRequest;
    private DateTimeOffset _cooldownUntil;
    private int _consecutiveRateLimits;

    public R18RequestScheduler() : this(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30),
        () => DateTimeOffset.UtcNow, Task.Delay) { }

    internal R18RequestScheduler(TimeSpan minimumInterval, TimeSpan maximumAutomaticWait,
        Func<DateTimeOffset> utcNow, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _minimumInterval = minimumInterval;
        _maximumAutomaticWait = maximumAutomaticWait;
        _utcNow = utcNow;
        _delay = delay;
    }

    internal async Task<string?> DownloadJsonAsync(HttpClient client, string url,
        TimeSpan requestTimeout, CancellationToken cancellationToken, Action<TimeSpan>? cooling)
    {
        // Two automatic retries of the SAME URL; a 429 never falls through to another URL.
        for (var attempt = 0; ; attempt++)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cooldown = _cooldownUntil - _utcNow();
                if (cooldown > _maximumAutomaticWait)
                {
                    throw new MetadataSourceRateLimitException(_cooldownUntil);
                }
                if (cooldown > TimeSpan.Zero)
                {
                    cooling?.Invoke(cooldown);
                }
                var due = _nextRequest > _cooldownUntil ? _nextRequest : _cooldownUntil;
                while (due > _utcNow())
                {
                    await _delay(due - _utcNow(), cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                _nextRequest = _utcNow() + _minimumInterval;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(requestTimeout);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Referrer = new Uri(R18DevClient.HomePageUrl);
                    using var response = await client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    // Status must be checked before content type: throttling pages are often HTML.
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _consecutiveRateLimits = Math.Min(_consecutiveRateLimits + 1, 6);
                        var backoff = TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, _consecutiveRateLimits - 1)));
                        var retryAfter = response.Headers.RetryAfter;
                        var serverWait = retryAfter?.Delta ??
                            (retryAfter?.Date - (response.Headers.Date ?? _utcNow())) ?? TimeSpan.Zero;
                        var wait = serverWait > backoff ? serverWait : backoff;
                        _cooldownUntil = _utcNow() + wait;
                        AppLog.Warning($"R18.dev 请求限流 status=429 retry={attempt}/2 cooldownSeconds={wait.TotalSeconds:0}");
                        if (attempt >= 2 || wait > _maximumAutomaticWait)
                        {
                            throw new MetadataSourceRateLimitException(_cooldownUntil);
                        }
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _consecutiveRateLimits = 0;
                        return null;
                    }
                    response.EnsureSuccessStatusCode();
                    _consecutiveRateLimits = 0;
                    if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return null;
                    }
                    return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new MetadataSourceTimeoutException("r18dev", "R18.dev", requestTimeout, exception);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}

public sealed class MetadataSourceRateLimitException(DateTimeOffset retryAt)
    : Exception("R18.dev 请求过于频繁（HTTP 429）；已保留其他来源资料，请稍后重试失败来源。")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

// Scheduled providers own per-request timeouts so waiting for their shared gate cannot time out.
internal interface IRequestScheduledMetadataProvider : IMetadataProvider
{
    Task<JavMetaLite.Core.Models.MovieMetadata> SearchWithRequestTimeoutAsync(
        string rawId, TimeSpan requestTimeout, CancellationToken cancellationToken);
}
