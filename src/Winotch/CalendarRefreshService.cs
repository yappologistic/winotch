using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Winotch;

public sealed class CalendarRefreshService : IDisposable
{
    private readonly Dictionary<string, CalendarFeedCache> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public CalendarRefreshService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(12) }, ownsHttpClient: true)
    {
    }

    public CalendarRefreshService(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private CalendarRefreshService(HttpClient httpClient, bool ownsHttpClient)
    {
        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<CalendarRefreshResult> RefreshAsync(
        IEnumerable<string> urls,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var events = new List<CalendarEvent>();
        DateTimeOffset? lastUpdated = null;
        foreach (var url in CalendarSubscriptionUrl.NormalizeAll(urls))
        {
            var cache = await RefreshFeedAsync(url, now, cancellationToken);
            if (cache is null)
            {
                continue;
            }

            events.AddRange(cache.Events);
            lastUpdated = lastUpdated is null || cache.LastUpdatedUtc > lastUpdated ? cache.LastUpdatedUtc : lastUpdated;
        }

        return new CalendarRefreshResult(events, lastUpdated);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task<CalendarFeedCache?> RefreshFeedAsync(
        string url,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _cache.TryGetValue(url, out var cached);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (cached?.ETag is { Length: > 0 } etag)
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
            }

            if (cached?.LastModifiedUtc is { } lastModified)
            {
                request.Headers.IfModifiedSince = lastModified;
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                cached.LastUpdatedUtc = now;
                return cached;
            }

            if (!response.IsSuccessStatusCode)
            {
                return cached;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var updated = new CalendarFeedCache(
                IcsParser.Parse(text),
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified,
                now);
            _cache[url] = updated;
            return updated;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            return cached;
        }
    }

    private sealed class CalendarFeedCache(
        IReadOnlyList<CalendarEvent> events,
        string? eTag,
        DateTimeOffset? lastModifiedUtc,
        DateTimeOffset lastUpdatedUtc)
    {
        public IReadOnlyList<CalendarEvent> Events { get; } = events;
        public string? ETag { get; } = eTag;
        public DateTimeOffset? LastModifiedUtc { get; } = lastModifiedUtc;
        public DateTimeOffset LastUpdatedUtc { get; set; } = lastUpdatedUtc;
    }
}
