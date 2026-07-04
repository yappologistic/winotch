using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Winotch;

public sealed class AppBarReservationService : IDisposable
{
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbeTop = 1;

    private static readonly uint CallbackMessage = RegisterWindowMessage("WinotchAppBarMessage");
    private IntPtr _handle;
    private bool _registered;

    public void ReserveTop(Window window, double heightDip, MonitorSnapshot monitor)
    {
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var registrationData = CreateData();
        if (!_registered && SHAppBarMessage(AbmNew, ref registrationData).ToUInt64() == 0)
        {
            return;
        }

        _registered = true;
        var heightPixels = ToPhysicalPixels(heightDip, monitor.DpiScaleY);
        var data = CreateData();
        data.Edge = AbeTop;
        data.Rect = new NativeRect(
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Right,
            monitor.Bounds.Top + heightPixels);

        SHAppBarMessage(AbmQueryPos, ref data);
        data.Rect.Top = monitor.Bounds.Top;
        data.Rect.Bottom = monitor.Bounds.Top + heightPixels;
        SHAppBarMessage(AbmSetPos, ref data);
    }

    public void Release()
    {
        if (!_registered)
        {
            return;
        }

        var data = CreateData();
        SHAppBarMessage(AbmRemove, ref data);
        _registered = false;
    }

    public void Dispose() => Release();

    public static int ToPhysicalPixels(double dip, double dpiScale) =>
        Math.Max(1, (int)Math.Ceiling(dip * Math.Max(0.1, dpiScale)));

    private AppBarData CreateData() => new()
    {
        Size = Marshal.SizeOf<AppBarData>(),
        WindowHandle = _handle,
        CallbackMessage = CallbackMessage
    };

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Param;
    }
}
