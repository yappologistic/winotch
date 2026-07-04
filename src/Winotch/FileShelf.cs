using System.IO;
using System.Text.Json;

namespace Winotch;

public sealed class FileShelf
{
    private readonly List<string> _paths = [];

    public FileShelf()
    {
    }

    public FileShelf(IEnumerable<string> paths)
    {
        Add(paths);
    }

    public IReadOnlyList<string> Paths => _paths.AsReadOnly();
    public int Count => _paths.Count;

    public bool Add(IEnumerable<string> paths)
    {
        var changed = false;
        var known = _paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Select(NormalizePath).Where(path => path is not null).Cast<string>())
        {
            if (!known.Add(path))
            {
                continue;
            }

            _paths.Add(path);
            changed = true;
        }

        return changed;
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public sealed class FileShelfStore
{
    private readonly string _path;

    public FileShelfStore()
        : this(DefaultPath)
    {
    }

    public FileShelfStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winotch", "shelf.json");

    public async Task<FileShelf> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new FileShelf();
            }

            await using var stream = File.OpenRead(_path);
            var document = await JsonSerializer.DeserializeAsync<FileShelfDocument>(stream);
            return new FileShelf(document?.Paths ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new FileShelf();
        }
    }

    public async Task SaveAsync(FileShelf shelf)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, new FileShelfDocument(shelf.Paths));
    }

    private sealed record FileShelfDocument(IReadOnlyList<string> Paths);
}
