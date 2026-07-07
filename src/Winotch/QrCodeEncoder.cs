using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrushes = System.Windows.Media.Brushes;

namespace Winotch;

public sealed record QrCode(int Version, int Size, bool[,] Modules, byte[] PayloadBytes, int DataCapacityBytes);

public static class QrCodeEncoder
{
    private const int Version = 1;
    private const int Size = 21;
    private const int DataCodewords = 19;
    private const int EccCodewords = 7;
    private const int ByteCapacity = 17;
    private const int Mask = 0;
    private const int DarkModuleRow = 13;
    private static readonly int[] Generator = [127, 122, 154, 164, 11, 68, 117];

    public static QrCode EncodeText(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        if (payload.Length > ByteCapacity)
        {
            throw new ArgumentException($"QR v1 supports up to {ByteCapacity} UTF-8 bytes.", nameof(text));
        }

        var data = BuildDataCodewords(payload);
        var codewords = data.Concat(ReedSolomon(data)).ToArray();
        var reserved = new bool[Size, Size];
        var modules = new bool[Size, Size];
        DrawFunctionPatterns(modules, reserved);
        DrawData(modules, reserved, codewords);
        DrawFormatBits(modules, reserved);
        return new QrCode(Version, Size, modules, payload, ByteCapacity);
    }

    public static BitmapSource Render(QrCode qr, int scale)
    {
        var border = 4;
        var pixels = (qr.Size + border * 2) * scale;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(MediaBrushes.White, null, new Rect(0, 0, pixels, pixels));
            for (var y = 0; y < qr.Size; y++)
            {
                for (var x = 0; x < qr.Size; x++)
                {
                    if (qr.Modules[y, x])
                    {
                        drawing.DrawRectangle(
                            MediaBrushes.Black,
                            null,
                            new Rect((x + border) * scale, (y + border) * scale, scale, scale));
                    }
                }
            }
        }

        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] BuildDataCodewords(byte[] payload)
    {
        var bits = new List<int>();
        AppendBits(bits, 0b0100, 4);
        AppendBits(bits, payload.Length, 8);
        foreach (var value in payload)
        {
            AppendBits(bits, value, 8);
        }

        var terminator = Math.Min(4, DataCodewords * 8 - bits.Count);
        AppendBits(bits, 0, terminator);
        while (bits.Count % 8 != 0)
        {
            bits.Add(0);
        }

        var codewords = new List<byte>();
        for (var i = 0; i < bits.Count; i += 8)
        {
            codewords.Add((byte)bits.Skip(i).Take(8).Aggregate(0, (value, bit) => (value << 1) | bit));
        }

        for (var pad = true; codewords.Count < DataCodewords; pad = !pad)
        {
            codewords.Add((byte)(pad ? 0xEC : 0x11));
        }

        return codewords.ToArray();
    }

    private static byte[] ReedSolomon(byte[] data)
    {
        var remainder = new byte[EccCodewords];
        foreach (var value in data)
        {
            var factor = value ^ remainder[0];
            Array.Copy(remainder, 1, remainder, 0, EccCodewords - 1);
            remainder[^1] = 0;
            for (var i = 0; i < EccCodewords; i++)
            {
                remainder[i] ^= GfMultiply(Generator[i], factor);
            }
        }

        return remainder;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] reserved)
    {
        DrawFinder(modules, reserved, 0, 0);
        DrawFinder(modules, reserved, Size - 7, 0);
        DrawFinder(modules, reserved, 0, Size - 7);
        for (var i = 0; i < Size; i++)
        {
            Reserve(reserved, 6, i);
            Reserve(reserved, i, 6);
            if (i is >= 8 and <= 12)
            {
                modules[6, i] = i % 2 == 0;
                modules[i, 6] = i % 2 == 0;
            }
        }

        Set(modules, reserved, 8, DarkModuleRow, true);
    }

    private static void DrawFinder(bool[,] modules, bool[,] reserved, int left, int top)
    {
        for (var y = -1; y <= 7; y++)
        {
            for (var x = -1; x <= 7; x++)
            {
                var row = top + y;
                var column = left + x;
                if (!InBounds(row, column))
                {
                    continue;
                }

                var black = x is >= 0 and <= 6 && y is >= 0 and <= 6 &&
                    (x is 0 or 6 || y is 0 or 6 || x is >= 2 and <= 4 && y is >= 2 and <= 4);
                Set(modules, reserved, column, row, black);
            }
        }
    }

    private static void DrawData(bool[,] modules, bool[,] reserved, byte[] codewords)
    {
        var bitIndex = 0;
        var upward = true;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right--;
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                var row = upward ? Size - 1 - vertical : vertical;
                for (var offset = 0; offset < 2; offset++)
                {
                    var column = right - offset;
                    if (reserved[row, column])
                    {
                        continue;
                    }

                    var bit = bitIndex < codewords.Length * 8 &&
                        ((codewords[bitIndex / 8] >> (7 - bitIndex % 8)) & 1) == 1;
                    bitIndex++;
                    modules[row, column] = bit ^ ((row + column) % 2 == Mask);
                }
            }

            upward = !upward;
        }
    }

    private static void DrawFormatBits(bool[,] modules, bool[,] reserved)
    {
        const int format = 0b111011111000100;
        int[] rowsA = [8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0];
        int[] colsA = [0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8];
        int[] rowsB = [20, 19, 18, 17, 16, 15, 14, 13, 8, 8, 8, 8, 8, 8, 8];
        int[] colsB = [8, 8, 8, 8, 8, 8, 8, 8, 13, 14, 15, 16, 17, 18, 20];
        for (var i = 0; i < 15; i++)
        {
            var bit = ((format >> i) & 1) == 1;
            Set(modules, reserved, colsA[i], rowsA[i], bit);
            Set(modules, reserved, colsB[i], rowsB[i], bit);
        }
    }

    private static void AppendBits(List<int> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            bits.Add((value >> i) & 1);
        }
    }

    private static void Set(bool[,] modules, bool[,] reserved, int column, int row, bool value)
    {
        modules[row, column] = value;
        reserved[row, column] = true;
    }

    private static void Reserve(bool[,] reserved, int row, int column)
    {
        if (InBounds(row, column))
        {
            reserved[row, column] = true;
        }
    }

    private static bool InBounds(int row, int column) =>
        row >= 0 && row < Size && column >= 0 && column < Size;

    private static byte GfMultiply(int x, int y)
    {
        var result = 0;
        for (; y > 0; y >>= 1)
        {
            if ((y & 1) != 0)
            {
                result ^= x;
            }

            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= 0x11D;
            }
        }

        return (byte)result;
    }
}
