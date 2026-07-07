namespace Winotch.Tests;

public class DropletTests
{
    [Fact]
    public void ColorPickerFormatsAndParsesHexAndRgb()
    {
        var color = new PickedColor(0x12, 0xAB, 0xEF);

        Assert.Equal("#12ABEF", color.Hex);
        Assert.Equal("rgb(18, 171, 239)", color.RgbText);
        Assert.True(PickedColor.TryParseHex("#12abef", out var parsed));
        Assert.Equal(color, parsed);
        Assert.False(PickedColor.TryParseHex("#nope", out _));
    }

    [Fact]
    public void TextScrubberTransformsCaseLineBreaksTrimAndCount()
    {
        var result = TextScrubberService.Scrub(
            "  hELLO\r\nWORLD  ",
            new TextScrubOptions(RemoveLineBreaks: true, Case: TextScrubCase.Sentence));

        Assert.Equal("Hello world", result.Text);
        Assert.Equal(11, result.CharacterCount);
    }

    [Theory]
    [InlineData(TextScrubCase.Upper, "HELLO WORLD")]
    [InlineData(TextScrubCase.Lower, "hello world")]
    [InlineData(TextScrubCase.Title, "Hello World")]
    public void TextScrubberAppliesSimpleCaseTransforms(TextScrubCase transform, string expected)
    {
        var result = TextScrubberService.Scrub(
            "hello WORLD",
            new TextScrubOptions(Case: transform));

        Assert.Equal(expected, result.Text);
    }
}
