using System.Text.RegularExpressions;

namespace Winotch;

public static class JoinLinkDetector
{
    private static readonly Regex LinkPattern = new(
        "https?://(?:[A-Za-z0-9.-]+\\.)?zoom\\.us/j/[^\\s<>\"']+|https?://teams\\.microsoft\\.com/l/meetup-join/[^\\s<>\"']+|https?://teams\\.live\\.com/[^\\s<>\"']+|https?://meet\\.google\\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}[^\\s<>\"']*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? FindFirst(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var match = LinkPattern.Match(field);
            if (match.Success)
            {
                return match.Value.TrimEnd('.', ',', ';', ')', ']', '>');
            }
        }

        return null;
    }
}
