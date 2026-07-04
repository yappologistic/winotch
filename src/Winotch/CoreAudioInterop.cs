using System.Runtime.InteropServices;

namespace Winotch;

internal enum AudioDataFlow
{
    Render,
    Capture
}

internal enum AudioRole
{
    Console,
    Multimedia,
    Communications
}

[Flags]
internal enum AudioDeviceState
{
    Active = 1
}

internal enum AudioSessionState
{
    Inactive,
    Active,
    Expired
}

internal static class CoreAudioInterop
{
    public const int ClsCtxAll = 23;
    public static readonly PropertyKey DeviceFriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static MMDeviceEnumeratorInterface CreateEnumerator() =>
        (MMDeviceEnumeratorInterface)(object)new MMDeviceEnumerator();

    public static bool Succeeded(int hresult) => hresult >= 0;

    public static string? ReadDeviceId(MMDeviceInterface device) =>
        Succeeded(device.GetId(out var id)) ? id : null;

    public static string ReadDeviceFriendlyName(MMDeviceInterface device)
    {
        PropertyStoreInterface? store = null;
        try
        {
            if (!Succeeded(device.OpenPropertyStore(0, out store)))
            {
                return "Audio device";
            }

            var key = DeviceFriendlyName;
            if (!Succeeded(store.GetValue(ref key, out var value)))
            {
                return "Audio device";
            }

            try
            {
                return value.GetString() ?? "Audio device";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(store);
        }
    }

    public static T? Activate<T>(MMDeviceInterface device)
    {
        try
        {
            var iid = typeof(T).GUID;
            return Succeeded(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var instance))
                ? (T)instance
                : default;
        }
        catch
        {
            return default;
        }
    }

    public static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, int PropertyId);

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    private readonly ushort _variantType;
    private readonly ushort _reserved1;
    private readonly ushort _reserved2;
    private readonly ushort _reserved3;
    private readonly IntPtr _value;

    public string? GetString() =>
        _variantType == 31 ? Marshal.PtrToStringUni(_value) : null;
}
