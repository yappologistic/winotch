namespace Winotch;

public static class CalendarSubscriptionUrl
{
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string>? urls)
    {
        if (urls is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (TryNormalize(url, out var value) && seen.Add(value))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    public static IReadOnlyList<string> FromMultiline(string text) =>
        NormalizeAll((text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None));

    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var value = url.Trim();
        if (value.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            value = string.Concat(Uri.UriSchemeHttps, "://", value["webcal://".Length..]);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        normalized = uri.ToString();
        return true;
    }
}
