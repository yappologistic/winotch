using System.IO;
using System.Security.Cryptography;

namespace Winotch;

public enum ClipboardHistoryKind
{
    Text,
    Link,
    Image,
    Files
}

public sealed record ClipboardHistoryEntry
{
    public const int MaxTextLength = 4096;
    private const int MaxPreviewLength = 80;

    private ClipboardHistoryEntry(
        ClipboardHistoryKind kind,
        string preview,
        string? text,
        IReadOnlyList<string> filePaths,
        byte[]? thumbnailPng,
        DateTimeOffset capturedAt,
        string signature)
    {
        Id = Guid.NewGuid();
        Kind = kind;
        Preview = preview;
        Text = text;
        FilePaths = filePaths;
        ThumbnailPng = thumbnailPng;
        CapturedAt = capturedAt;
        Signature = signature;
    }

    public Guid Id { get; }
    public ClipboardHistoryKind Kind { get; }
    public string Preview { get; }
    public string? Text { get; }
    public IReadOnlyList<string> FilePaths { get; }
    public byte[]? ThumbnailPng { get; }
    public DateTimeOffset CapturedAt { get; }
    public string Signature { get; }

    public static ClipboardHistoryEntry? FromText(string? text, DateTimeOffset capturedAt)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var capped = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        var trimmed = capped.Trim();
        var isLink = IsHttpUrl(trimmed);
        var preview = isLink ? trimmed : FirstLine(capped);
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "(blank text)";
        }

        var kind = isLink ? ClipboardHistoryKind.Link : ClipboardHistoryKind.Text;
        return new ClipboardHistoryEntry(
            kind,
            Truncate(preview),
            capped,
            [],
            null,
            capturedAt,
            $"{kind}\u001f{capped}");
    }

    public static ClipboardHistoryEntry? FromFiles(IEnumerable<string> paths, DateTimeOffset capturedAt)
    {
        var files = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
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
        return new ClipboardHistoryEntry(
            ClipboardHistoryKind.Files,
            Truncate(preview),
            null,
            Array.AsReadOnly(files),
            null,
            capturedAt,
            $"Files\u001f{string.Join('\u001e', files)}");
    }

    public static ClipboardHistoryEntry? FromImage(byte[]? thumbnailPng, DateTimeOffset capturedAt)
    {
        if (thumbnailPng is null || thumbnailPng.Length == 0)
        {
            return null;
        }

        var copy = thumbnailPng.ToArray();
        return new ClipboardHistoryEntry(
            ClipboardHistoryKind.Image,
            "Image",
            null,
            [],
            copy,
            capturedAt,
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
