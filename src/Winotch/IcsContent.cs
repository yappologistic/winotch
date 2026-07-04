namespace Winotch;

internal sealed record IcsProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value);

internal static class IcsContent
{
    public static IReadOnlyList<IcsProperty> ReadProperties(string text) =>
        UnfoldLines(text).Select(ParseProperty).Where(property => property is not null).Cast<IcsProperty>().ToArray();

    public static string UnescapeText(string value)
    {
        return value
            .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",", StringComparison.Ordinal)
            .Replace("\\;", ";", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static IEnumerable<string> UnfoldLines(string text)
    {
        var current = string.Empty;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (rawLine.StartsWith(' ') || rawLine.StartsWith('\t'))
            {
                current += rawLine[1..];
                continue;
            }

            if (current.Length > 0)
            {
                yield return current;
            }

            current = rawLine;
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private static IcsProperty? ParseProperty(string line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        var nameAndParameters = line[..separator].Split(';');
        var name = nameAndParameters[0].Trim().ToUpperInvariant();
        if (name.Length == 0)
        {
            return null;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in nameAndParameters.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            parameters[part[..equals].Trim()] = part[(equals + 1)..].Trim().Trim('"');
        }

        return new IcsProperty(name, parameters, line[(separator + 1)..]);
    }
}
