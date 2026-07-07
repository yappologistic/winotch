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
    public void QrEncoderBuildsVersionOneMatrixForShortText()
    {
        var qr = QrCodeEncoder.EncodeText("Winotch");

        Assert.Equal(1, qr.Version);
        Assert.Equal(21, qr.Size);
        Assert.Equal(17, qr.DataCapacityBytes);
        Assert.Equal("Winotch"u8.ToArray(), qr.PayloadBytes);
        Assert.True(qr.Modules[0, 0]);
        Assert.True(qr.Modules[6, 6]);
        Assert.True(qr.Modules[13, 8]);
    }

    [Fact]
    public void QrEncoderRejectsTextBeyondVersionOneCapacity()
    {
        var text = new string('x', 18);

        Assert.Throws<ArgumentException>(() => QrCodeEncoder.EncodeText(text));
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
