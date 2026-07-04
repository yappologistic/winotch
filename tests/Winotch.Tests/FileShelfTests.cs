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
    public void PathsCannotBeMutatedThroughReadOnlyView()
    {
        var shelf = new FileShelf([Path.Combine(Path.GetTempPath(), "WinotchShelf.txt")]);
        var paths = Assert.IsAssignableFrom<IList<string>>(shelf.Paths);

        Assert.Throws<NotSupportedException>(() => paths.Add(Path.Combine(Path.GetTempPath(), "other.txt")));
        Assert.Single(shelf.Paths);
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

}
