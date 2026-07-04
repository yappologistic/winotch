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

    public IReadOnlyList<string> Paths => _paths;
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

    public bool Remove(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
        {
            return false;
        }

        var index = _paths.FindIndex(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _paths.RemoveAt(index);
        return true;
    }

    public bool Clear()
    {
        if (_paths.Count == 0)
        {
            return false;
        }

        _paths.Clear();
        return true;
    }

    public FileShelfSnapshot CreateSnapshot(int visibleLimit) => FileShelfSnapshot.Create(_paths, visibleLimit);

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

public sealed record FileShelfSnapshot(
    IReadOnlyList<FileShelfTile> Tiles,
    int TotalCount,
    int OverflowCount)
{
    public bool HasItems => TotalCount > 0;

    public static FileShelfSnapshot Create(IReadOnlyList<string> paths, int visibleLimit)
    {
        var limit = Math.Max(0, visibleLimit);
        var tiles = paths.Take(limit).Select(FileShelfTile.FromPath).ToArray();
        return new FileShelfSnapshot(tiles, paths.Count, Math.Max(0, paths.Count - limit));
    }
}

public sealed record FileShelfTile(
    string FullPath,
    string DisplayName,
    bool Exists,
    bool IsDirectory)
{
    public static FileShelfTile FromPath(string path)
    {
        var isDirectory = Directory.Exists(path);
        var exists = isDirectory || File.Exists(path);
        return new FileShelfTile(
            path,
            FileShelfDisplay.NameFromPath(path),
            exists,
            isDirectory);
    }
}

public static class FileShelfDisplay
{
    public const int DefaultMaxNameLength = 24;

    public static string NameFromPath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return TruncateName(string.IsNullOrWhiteSpace(name) ? path : name);
    }

    public static string TruncateName(string name, int maxLength = DefaultMaxNameLength)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "Item" : name.Trim();
        if (value.Length <= maxLength || maxLength < 8)
        {
            return value;
        }

        var extension = Path.GetExtension(value);
        if (extension.Length is > 0 and <= 8 && maxLength > extension.Length + 4)
        {
            var prefixLength = maxLength - extension.Length - 3;
            return string.Concat(value.AsSpan(0, prefixLength), "...", extension);
        }

        var headLength = (maxLength - 3) / 2;
        var tailLength = maxLength - 3 - headLength;
        return string.Concat(value.AsSpan(0, headLength), "...", value.AsSpan(value.Length - tailLength));
    }
}

public sealed class FileShelfStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

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
            var document = await JsonSerializer.DeserializeAsync<FileShelfDocument>(stream, JsonOptions);
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
        await JsonSerializer.SerializeAsync(stream, new FileShelfDocument(shelf.Paths), JsonOptions);
    }

    private sealed record FileShelfDocument(IReadOnlyList<string> Paths);
}
