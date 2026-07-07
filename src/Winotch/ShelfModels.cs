using System.IO;
using System.Security.Cryptography;

namespace Winotch;

public enum ShelfItemKind
{
    Text,
    Link,
    Files,
    Image
}

public sealed record ShelfItem
{
    public const int MaxTextLength = 4096;
    private const int MaxPreviewLength = 80;

    private ShelfItem(
        ShelfItemKind kind,
        string preview,
        string? text,
        IReadOnlyList<string> filePaths,
        byte[]? thumbnailPng,
        DateTimeOffset stagedAt,
        string signature)
    {
        Id = Guid.NewGuid();
        Kind = kind;
        Preview = preview;
        Text = text;
        FilePaths = filePaths;
        ThumbnailPng = thumbnailPng;
        StagedAt = stagedAt;
        Signature = signature;
    }

    public Guid Id { get; }
    public ShelfItemKind Kind { get; }
    public string Preview { get; }
    public string? Text { get; }
    public IReadOnlyList<string> FilePaths { get; }
    public byte[]? ThumbnailPng { get; }
    public DateTimeOffset StagedAt { get; }
    public string Signature { get; }

    public static ShelfItem? FromText(string? text, DateTimeOffset stagedAt)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var capped = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        var trimmed = capped.Trim();
        var kind = IsHttpUrl(trimmed) ? ShelfItemKind.Link : ShelfItemKind.Text;
        var preview = kind == ShelfItemKind.Link ? trimmed : FirstLine(capped);
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "(blank text)";
        }

        return new ShelfItem(
            kind,
            Truncate(preview),
            capped,
            [],
            null,
            stagedAt,
            $"{kind}\u001f{capped}");
    }

    public static ShelfItem? FromFiles(IEnumerable<string> paths, DateTimeOffset stagedAt)
    {
        var files = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (files.Length == 0)
        {
            return null;
        }

        var firstName = Path.GetFileName(files[0]);
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = files[0];
        }

        var preview = files.Length == 1 ? firstName : $"{firstName} +{files.Length - 1}";
        return new ShelfItem(
            ShelfItemKind.Files,
            Truncate(preview),
            null,
            Array.AsReadOnly(files),
            null,
            stagedAt,
            $"Files\u001f{string.Join('\u001e', files)}");
    }

    public static ShelfItem? FromImage(byte[]? thumbnailPng, DateTimeOffset stagedAt)
    {
        if (thumbnailPng is null || thumbnailPng.Length == 0)
        {
            return null;
        }

        var copy = thumbnailPng.ToArray();
        return new ShelfItem(
            ShelfItemKind.Image,
            "Image",
            null,
            [],
            copy,
            stagedAt,
            $"Image\u001f{Convert.ToHexString(SHA256.HashData(copy))}");
    }

    private static bool IsHttpUrl(string text) =>
        !text.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string FirstLine(string text)
    {
        var lineEnd = text.IndexOfAny(['\r', '\n']);
        return lineEnd < 0 ? text.Trim() : text[..lineEnd].Trim();
    }

    private static string Truncate(string value) =>
        value.Length <= MaxPreviewLength ? value : $"{value[..(MaxPreviewLength - 3)]}...";
}
