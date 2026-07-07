using System.Globalization;

namespace Winotch.CommandBar;

public static class CommandMatch
{
    public static double Score(string query, string candidate)
    {
        var needle = Normalize(query);
        var haystack = Normalize(candidate);
        if (needle.Length == 0 || haystack.Length == 0)
        {
            return 0;
        }

        if (StringComparer.Ordinal.Equals(needle, haystack))
        {
            return 100;
        }

        if (haystack.StartsWith(needle, StringComparison.Ordinal))
        {
            return 92 - Math.Min(12, haystack.Length - needle.Length);
        }

        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            return 76 - Math.Min(10, haystack.Length - needle.Length);
        }

        var subsequence = SubsequenceScore(needle, haystack);
        if (subsequence <= 0)
        {
            return 0;
        }

        var tokenBonus = haystack
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.StartsWith(needle, StringComparison.Ordinal))
            ? 8
            : 0;
        return Math.Min(72, subsequence + tokenBonus);
    }

    public static double Rank(double nameScore, int providerPriority) =>
        nameScore <= 0 ? 0 : nameScore + providerPriority / 100.0;

    private static double SubsequenceScore(string needle, string haystack)
    {
        var matched = 0;
        var gaps = 0;
        var last = -1;
        for (var i = 0; i < haystack.Length && matched < needle.Length; i++)
        {
            if (haystack[i] != needle[matched])
            {
                continue;
            }

            if (last >= 0)
            {
                gaps += i - last - 1;
            }

            last = i;
            matched++;
        }

        if (matched != needle.Length)
        {
            return 0;
        }

        // Compact subsequence matches feel closer to direct command names than sparse acronym hits.
        return Math.Max(1, 68 - gaps * 3 - Math.Max(0, haystack.Length - needle.Length) * 0.4);
    }

    private static string Normalize(string value)
    {
        var chars = new List<char>(value.Length);
        foreach (var c in value.Trim().ToLower(CultureInfo.CurrentCulture))
        {
            chars.Add(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

