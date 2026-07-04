namespace Winotch.Tests;

public sealed class FileShelfTests
{
    [Fact]
    public void AddDeduplicatesPathsCaseInsensitively()
    {
        var shelf = new FileShelf();
        var path = Path.Combine(Path.GetTempPath(), "WinotchShelf.txt");

        shelf.Add([path, path.ToUpperInvariant()]);

        var stored = Assert.Single(shelf.Paths);
        Assert.Equal(Path.GetFullPath(path), stored);
    }

    [Fact]
    public async Task PersistenceRoundTripsStoredPaths()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}", "shelf.json");
        var store = new FileShelfStore(storePath);
        var file = Path.Combine(Path.GetTempPath(), "roundtrip.txt");
        var folder = Path.Combine(Path.GetTempPath(), "roundtrip-folder");
        var shelf = new FileShelf([file, folder]);

        await store.SaveAsync(shelf);
        var loaded = await store.LoadAsync();

        Assert.Equal(shelf.Paths, loaded.Paths);
    }

    [Fact]
    public async Task PersistenceFallsBackToEmptyWhenJsonIsCorrupt()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}", "shelf.json");
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        await File.WriteAllTextAsync(storePath, "{not json");
        var store = new FileShelfStore(storePath);

        var loaded = await store.LoadAsync();

        Assert.Empty(loaded.Paths);
    }

    [Fact]
    public void TileClassifiesMissingFiles()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");

        var tile = FileShelfTile.FromPath(missing);

        Assert.False(tile.Exists);
        Assert.False(tile.IsDirectory);
    }

    [Fact]
    public void TileClassifiesDirectories()
    {
        var folder = Directory.CreateTempSubdirectory("winotch-shelf-");

        try
        {
            var tile = FileShelfTile.FromPath(folder.FullName);

            Assert.True(tile.Exists);
            Assert.True(tile.IsDirectory);
        }
        finally
        {
            folder.Delete();
        }
    }

    [Theory]
    [InlineData("short.txt", 24, "short.txt")]
    [InlineData("averyverylongfilename.txt", 18, "averyverylo....txt")]
    [InlineData("averyverylongfilenamewithoutextension", 18, "averyve...xtension")]
    public void DisplayNameTruncatesLongNamesPredictably(string name, int maxLength, string expected)
    {
        Assert.Equal(expected, FileShelfDisplay.TruncateName(name, maxLength));
    }

    [Fact]
    public void SnapshotCapsVisibleTilesAndReportsOverflow()
    {
        var paths = Enumerable.Range(1, 5)
            .Select(index => Path.Combine(Path.GetTempPath(), $"file-{index}.txt"));
        var shelf = new FileShelf(paths);

        var snapshot = shelf.CreateSnapshot(3);

        Assert.Equal(5, snapshot.TotalCount);
        Assert.Equal(2, snapshot.OverflowCount);
        Assert.Equal(3, snapshot.Tiles.Count);
    }
}
