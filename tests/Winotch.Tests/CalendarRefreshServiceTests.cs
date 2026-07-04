using System.Net;
using System.Net.Http;

namespace Winotch.Tests;

public class CalendarRefreshServiceTests
{
    [Fact]
    public async Task RefreshSendsConditionalHeadersAndReusesCachedEventsOnNotModified()
    {
        var now = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        var handler = new QueueHandler(
            _ => Response(HttpStatusCode.OK, Ics("standup"), "\"abc\"", now.AddHours(-1)),
            _ => new HttpResponseMessage(HttpStatusCode.NotModified));
        using var service = new CalendarRefreshService(new HttpClient(handler));

        var first = await service.RefreshAsync(["webcal://example.com/feed.ics"], now);
        var second = await service.RefreshAsync(["webcal://example.com/feed.ics"], now.AddMinutes(5));

        Assert.Single(first.Events);
        Assert.Single(second.Events);
        Assert.Equal("https://example.com/feed.ics", handler.Requests[0].Url);
        Assert.Equal("\"abc\"", handler.Requests[1].IfNoneMatch);
        Assert.Equal(now.AddHours(-1), handler.Requests[1].IfModifiedSince);
        Assert.Equal(now.AddMinutes(5), second.LastUpdatedUtc);
    }

    [Fact]
    public async Task RefreshKeepsLastGoodDataOnHttpError()
    {
        var now = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        var handler = new QueueHandler(
            _ => Response(HttpStatusCode.OK, Ics("standup"), null, null),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var service = new CalendarRefreshService(new HttpClient(handler));

        _ = await service.RefreshAsync(["https://example.com/feed.ics"], now);
        var result = await service.RefreshAsync(["https://example.com/feed.ics"], now.AddMinutes(5));

        var calendarEvent = Assert.Single(result.Events);
        Assert.Equal("standup", calendarEvent.Uid);
        Assert.Equal(now, result.LastUpdatedUtc);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string? etag, DateTimeOffset? lastModified)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (etag is not null)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        }

        response.Content.Headers.LastModified = lastModified;
        return response;
    }

    private static string Ics(string uid) => string.Join("\n",
        "BEGIN:VCALENDAR",
        "BEGIN:VEVENT",
        $"UID:{uid}",
        "DTSTART:20260704T160000Z",
        "SUMMARY:Standup",
        "END:VEVENT",
        "END:VCALENDAR");

    private sealed class QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.IfNoneMatch.ToString(),
                request.Headers.IfModifiedSince));
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed record RequestSnapshot(string Url, string IfNoneMatch, DateTimeOffset? IfModifiedSince);
}
