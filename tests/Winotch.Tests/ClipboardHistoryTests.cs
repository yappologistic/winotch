namespace Winotch.Tests;

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StoreKeepsNewestFirstAndCapsHistory()
    {
        var store = new ClipboardHistoryStore();

        for (var i = 0; i < 12; i++)
        {
            store.Push(Text($"item {i}"));
        }

        Assert.Equal(ClipboardHistoryStore.Capacity, store.Items.Count);
        Assert.Equal("item 11", store.Items[0].Preview);
        Assert.Equal("item 2", store.Items[^1].Preview);
    }

    [Fact]
    public void StoreDedupesOnlyConsecutiveEntries()
    {
        var store = new ClipboardHistoryStore();

        store.Push(Text("same", Now));
        store.Push(Text("same", Now.AddMinutes(1)));
        store.Push(Text("other", Now.AddMinutes(2)));
        store.Push(Text("same", Now.AddMinutes(3)));

        Assert.Collection(
            store.Items,
            first => Assert.Equal("same", first.Preview),
            second => Assert.Equal("other", second.Preview),
            third => Assert.Equal("same", third.Preview));
    }

    [Fact]
    public void StoreDeletesAndClearsEntries()
    {
        var store = new ClipboardHistoryStore();
        var first = Text("first");
        var second = Text("second");
        store.Push(first);
        store.Push(second);

        Assert.True(store.Delete(first.Id));
        Assert.Single(store.Items);
        Assert.True(store.Clear());
        Assert.Empty(store.Items);
        Assert.False(store.Clear());
    }

    [Fact]
    public void TextPreviewUsesFirstLineAndCapsStoredPayload()
    {
        var huge = $"{new string('a', 120)}\r\n{new string('b', 5000)}";

        var item = ClipboardHistoryEntry.FromText(huge, Now);

        Assert.NotNull(item);
        Assert.Equal(ClipboardHistoryKind.Text, item.Kind);
        Assert.Equal(ClipboardHistoryEntry.MaxTextLength, item.Text!.Length);
        Assert.EndsWith("...", item.Preview);
        Assert.DoesNotContain("b", item.Preview);
        Assert.True(item.Preview.Length <= 80);
    }

    [Theory]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://localhost:5000")]
    public void UrlTextIsClassifiedAsLink(string url)
    {
        var item = ClipboardHistoryEntry.FromText($"  {url}  ", Now);

        Assert.NotNull(item);
        Assert.Equal(ClipboardHistoryKind.Link, item.Kind);
        Assert.Equal(url, item.Preview);
    }

    [Fact]
    public void EmptyClipboardTextDoesNotCreateHistoryEntry()
    {
        Assert.Null(ClipboardHistoryEntry.FromText(null, Now));
        Assert.Null(ClipboardHistoryEntry.FromText("", Now));
        Assert.Null(ClipboardHistoryEntry.FromFiles([], Now));
        Assert.Null(ClipboardHistoryEntry.FromImage(null, Now));
    }

    [Fact]
    public void FilePreviewSummarizesMultiplePathsWithoutCheckingDisk()
    {
        var files = new[]
        {
            @"C:\missing\report.docx",
            @"C:\missing\image.png",
            @"C:\missing\notes.txt"
        };

        var item = ClipboardHistoryEntry.FromFiles(files, Now);

        Assert.NotNull(item);
        Assert.Equal(ClipboardHistoryKind.Files, item.Kind);
        Assert.Equal("report.docx +2", item.Preview);
        Assert.Equal(files, item.FilePaths);
    }

    [Fact]
    public void ImageEntryStoresOnlyThumbnailBytes()
    {
        var bytes = new byte[] { 1, 2, 3 };

        var item = ClipboardHistoryEntry.FromImage(bytes, Now);
        bytes[0] = 9;

        Assert.NotNull(item);
        Assert.Equal(ClipboardHistoryKind.Image, item.Kind);
        Assert.Equal("Image", item.Preview);
        Assert.Equal([1, 2, 3], item.ThumbnailPng);
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h")]
    [InlineData(86399, "23h")]
    [InlineData(86400, "1d")]
    public void RelativeTimeUsesCompactBoundaries(int secondsAgo, string expected)
    {
        Assert.Equal(expected, ClipboardHistoryFormatting.RelativeTime(Now.AddSeconds(-secondsAgo), Now));
    }

    [Fact]
    public void PrivacyPolicySkipsExplicitMonitorExclusionFormat()
    {
        var formats = new FakeClipboardFormats()
            .With(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);

        Assert.False(ClipboardPrivacyPolicy.CanCapture(formats));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void PrivacyPolicyHonorsCanIncludeClipboardHistoryDword(int value, bool expected)
    {
        var formats = new FakeClipboardFormats()
            .With(ClipboardPrivacyPolicy.CanIncludeInClipboardHistory, BitConverter.GetBytes(value));

        Assert.Equal(expected, ClipboardPrivacyPolicy.CanCapture(formats));
    }

    [Fact]
    public void PrivacyPolicyCapturesWhenNoExclusionFormatsArePresent()
    {
        Assert.True(ClipboardPrivacyPolicy.CanCapture(new FakeClipboardFormats()));
    }

    [Fact]
    public void UpdateQueueIgnoresSelfCopySequence()
    {
        var queue = new ClipboardUpdateQueue();
        queue.IgnoreSequence(42);

        Assert.False(queue.Enqueue(42));
        Assert.Null(queue.Consume());
        Assert.True(queue.Enqueue(43));
        Assert.Equal((uint)43, queue.Consume());
    }

    [Fact]
    public void UpdateQueueCoalescesRapidUpdatesToLatestSequence()
    {
        var queue = new ClipboardUpdateQueue();

        Assert.True(queue.Enqueue(10));
        Assert.True(queue.Enqueue(11));
        Assert.True(queue.Enqueue(12));

        Assert.Equal((uint)12, queue.Consume());
        Assert.Null(queue.Consume());
    }

    private static ClipboardHistoryEntry Text(string value, DateTimeOffset? capturedAt = null) =>
        ClipboardHistoryEntry.FromText(value, capturedAt ?? Now)!;

    private sealed class FakeClipboardFormats : IClipboardFormatReader
    {
        private readonly Dictionary<string, object?> _formats = new(StringComparer.Ordinal);

        public FakeClipboardFormats With(string formatName, object? value)
        {
            _formats[formatName] = value;
            return this;
        }

        public bool HasFormat(string formatName) =>
            _formats.ContainsKey(formatName);

        public bool TryGetData(string formatName, out object? data) =>
            _formats.TryGetValue(formatName, out data);
    }
}
