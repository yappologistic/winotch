using System.Globalization;
using System.Text;

namespace Winotch;

public enum TextScrubCase
{
    Preserve,
    Upper,
    Lower,
    Title,
    Sentence
}

public sealed record TextScrubOptions(
    bool TrimWhitespace = true,
    bool RemoveLineBreaks = false,
    TextScrubCase Case = TextScrubCase.Preserve);

public sealed record TextScrubResult(string Text, int CharacterCount);

public static class TextScrubberService
{
    public static TextScrubResult Scrub(string? input, TextScrubOptions options)
    {
        var text = input ?? string.Empty;
        if (options.RemoveLineBreaks)
        {
            text = CollapseLineBreaks(text);
        }

        if (options.TrimWhitespace)
        {
            text = text.Trim();
        }

        text = options.Case switch
        {
            TextScrubCase.Upper => text.ToUpperInvariant(),
            TextScrubCase.Lower => text.ToLowerInvariant(),
            TextScrubCase.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower()),
            TextScrubCase.Sentence => ToSentenceCase(text),
            _ => text
        };

        return new TextScrubResult(text, text.Length);
    }

    private static string CollapseLineBreaks(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ToSentenceCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        var capitalizeNext = true;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetter(character) && capitalizeNext)
            {
                builder.Append(char.ToUpperInvariant(character));
                capitalizeNext = false;
                continue;
            }

            builder.Append(character);
            if (character is '.' or '!' or '?')
            {
                capitalizeNext = true;
            }
        }

        return builder.ToString();
    }
}
