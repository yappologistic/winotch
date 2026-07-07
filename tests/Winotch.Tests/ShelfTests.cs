namespace Winotch.Tests;

public class ShelfTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceCapsNewestItemsAtConfiguredLimit()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 3 });

        for (var i = 0; i < 5; i++)
        {
            shelf.Stage(Text($"item {i}"));
        }

        Assert.Equal(3, shelf.Items.Count);
        Assert.Equal("item 4", shelf.Items[0].Preview);
        Assert.Equal("item 2", shelf.Items[^1].Preview);
    }

    [Fact]
    public void ServiceDedupesMatchingSignaturesAcrossShelf()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });

        shelf.Stage(Text("same", Now));
        shelf.Stage(Text("other", Now.AddMinutes(1)));
        shelf.Stage(Text("same", Now.AddMinutes(2)));

        Assert.Collection(
            shelf.Items,
            first => Assert.Equal("same", first.Preview),
            second => Assert.Equal("other", second.Preview));
    }

    [Fact]
    public void ServiceRemovesAndClearsItems()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });
        var first = Text("first");
        var second = Text("second");
        shelf.Stage(first);
        shelf.Stage(second);

        Assert.True(shelf.Remove(first.Id));
        Assert.Single(shelf.Items);
        Assert.True(shelf.Clear());
        Assert.Empty(shelf.Items);
        Assert.False(shelf.Clear());
    }

    [Fact]
    public void ServiceHonorsClipboardPrivacyExclusions()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });
        var formats = new FakeClipboardFormats()
            .With(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);

        Assert.False(shelf.Stage(Text("private"), formats));
        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void TextAndLinksAreClassifiedSeparately()
    {
        var text = ShelfItem.FromText("note", Now);
        var link = ShelfItem.FromText(" https://example.com/a ", Now);

        Assert.Equal(ShelfItemKind.Text, text!.Kind);
        Assert.Equal(ShelfItemKind.Link, link!.Kind);
        Assert.Equal("https://example.com/a", link.Preview);
    }

    [Fact]
    public void FileItemStoresOnlyPathsAndImageStoresOnlyThumbnail()
    {
        var files = ShelfItem.FromFiles([@"C:\temp\report.pdf", @"C:\temp\image.png"], Now);
        var thumbnail = new byte[] { 1, 2, 3 };
        var image = ShelfItem.FromImage(thumbnail, Now);
        thumbnail[0] = 9;

        Assert.NotNull(files);
        Assert.Equal(ShelfItemKind.Files, files.Kind);
        Assert.Null(files.ThumbnailPng);
        Assert.Equal("report.pdf +1", files.Preview);

        Assert.NotNull(image);
        Assert.Equal(ShelfItemKind.Image, image.Kind);
        Assert.Empty(image.FilePaths);
        Assert.Equal([1, 2, 3], image.ThumbnailPng);
    }

    private static ShelfItem Text(string value, DateTimeOffset? stagedAt = null) =>
        ShelfItem.FromText(value, stagedAt ?? Now)!;

    private sealed class FakeClipboardFormats : IClipboardFormatReader
    {
        private readonly Dictionary<string, object?> _formats = new(StringComparer.Ordinal);

        public FakeClipboardFormats With(string formatName, object? value)
        {
            _formats[formatName] = value;
            return this;
        }

        public bool HasFormat(string formatName) => _formats.ContainsKey(formatName);

        public bool TryGetData(string formatName, out object? data) =>
            _formats.TryGetValue(formatName, out data);
    }
}
