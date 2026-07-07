using System.Drawing;

namespace Winotch;

public sealed record PickedColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string RgbText => $"rgb({R}, {G}, {B})";

    public static bool TryParseHex(string value, out PickedColor color)
    {
        color = default!;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6 ||
            !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new PickedColor(r, g, b);
        return true;
    }
}

public sealed class ColorPickerService
{
    public PickedColor PickScreenPixel(int screenX, int screenY)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screenX, screenY, 0, 0, new Size(1, 1));
        var color = bitmap.GetPixel(0, 0);
        return new PickedColor(color.R, color.G, color.B);
    }
}
