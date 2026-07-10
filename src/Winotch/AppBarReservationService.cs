using System.Runtime.InteropServices;

namespace Winotch;

public sealed class AppBarReservationService : IDisposable
{
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbeTop = 1;
    private const ulong AbnPosChanged = 1;

    private static readonly uint CallbackMessage = RegisterWindowMessage("WinotchAppBarMessage");
    private IntPtr _handle;
    private WindowMessageSink? _messageSink;
    private MonitorSnapshot _monitor;
    private double _heightDip;
    private bool _registered;

    public void ReserveTop(FluentWindow window, double heightDip, MonitorSnapshot monitor)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (_registered && handle != _handle)
        {
            Release();
        }

        _handle = handle;
        _heightDip = heightDip;
        _monitor = monitor;

        if (_messageSink is null)
        {
            try
            {
                _messageSink = new WindowMessageSink(_handle, HandleWindowMessage);
            }
            catch
            {
                _handle = IntPtr.Zero;
                return;
            }
        }

        var registrationData = CreateData();
        if (!_registered && SHAppBarMessage(AbmNew, ref registrationData).ToUInt64() == 0)
        {
            _messageSink.Dispose();
            _messageSink = null;
            _handle = IntPtr.Zero;
            return;
        }

        _registered = true;
        SetReservationPosition();
    }

    private void SetReservationPosition()
    {
        if (!_registered || _handle == IntPtr.Zero)
        {
            return;
        }

        var heightPixels = ToPhysicalPixels(_heightDip, _monitor.DpiScaleY);
        var data = CreateData();
        data.Edge = AbeTop;
        data.Rect = new NativeRect(
            _monitor.Bounds.Left,
            _monitor.Bounds.Top,
            _monitor.Bounds.Right,
            _monitor.Bounds.Top + heightPixels);

        _ = SHAppBarMessage(AbmQueryPos, ref data);
        data.Rect = ApplyTopReservationHeight(data.Rect, heightPixels);
        _ = SHAppBarMessage(AbmSetPos, ref data);
    }

    public void Release()
    {
        if (_registered)
        {
            var data = CreateData();
            _ = SHAppBarMessage(AbmRemove, ref data);
        }

        _registered = false;
        _messageSink?.Dispose();
        _messageSink = null;
        _handle = IntPtr.Zero;
    }

    public void Dispose() => Release();

    public static int ToPhysicalPixels(double dip, double dpiScale) =>
        Math.Max(1, (int)Math.Ceiling(dip * (dpiScale > 0 ? dpiScale : 1)));

    public static NativeRect ApplyTopReservationHeight(NativeRect rect, int heightPixels) =>
        new(rect.Left, rect.Top, rect.Right, rect.Top + heightPixels);

    private AppBarData CreateData() => new()
    {
        Size = Marshal.SizeOf<AppBarData>(),
        WindowHandle = _handle,
        CallbackMessage = CallbackMessage
    };

    private bool HandleWindowMessage(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message != CallbackMessage)
        {
            return false;
        }

        if (wParam.ToUInt64() == AbnPosChanged)
        {
            SetReservationPosition();
        }

        return true;
    }

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
