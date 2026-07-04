using System.IO;

namespace Winotch;

public interface IClipboardFormatReader
{
    bool HasFormat(string formatName);
    bool TryGetData(string formatName, out object? data);
}

public static class ClipboardPrivacyPolicy
{
    public const string ExcludeClipboardContentFromMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";

    public static bool CanCapture(IClipboardFormatReader formats)
    {
        if (formats.HasFormat(ExcludeClipboardContentFromMonitorProcessing))
        {
            return false;
        }

        if (!formats.TryGetData(CanIncludeInClipboardHistory, out var data))
        {
            return true;
        }

        return ReadDword(data) > 0;
    }

    private static int? ReadDword(object? data) => data switch
    {
        int value => value,
        uint value => unchecked((int)value),
        byte[] bytes => ReadDword(bytes),
        MemoryStream stream => ReadDword(stream.ToArray()),
        Stream stream => ReadDword(ReadFirstBytes(stream, sizeof(int))),
        _ => null
    };

    private static int? ReadDword(byte[] bytes) =>
        bytes.Length < sizeof(int) ? null : BitConverter.ToInt32(bytes, 0);

    private static byte[] ReadFirstBytes(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        return read == count ? buffer : [];
    }
}
