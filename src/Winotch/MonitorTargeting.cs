using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Winotch;

public readonly record struct MonitorSnapshot(
    string DeviceName,
    NativeRect Bounds,
    NativeRect WorkingArea,
    bool IsPrimary,
    double DpiScaleX,
    double DpiScaleY)
{
    public double LeftDip => ToDip(Bounds.Left, DpiScaleX);
    public double TopDip => ToDip(Bounds.Top, DpiScaleY);
    public double WidthDip => ToDip(Bounds.Width, DpiScaleX);
    public double WorkAreaLeftDip => ToDip(WorkingArea.Left, DpiScaleX);
    public double WorkAreaTopDip => ToDip(WorkingArea.Top, DpiScaleY);
    public double WorkAreaRightDip => ToDip(WorkingArea.Right, DpiScaleX);
    public double WorkAreaBottomDip => ToDip(WorkingArea.Bottom, DpiScaleY);

    public bool Contains(Point point) =>
        point.X >= Bounds.Left &&
        point.X < Bounds.Right &&
        point.Y >= Bounds.Top &&
        point.Y < Bounds.Bottom;

    private static double ToDip(int value, double scale) => value / (scale > 0 ? scale : 1);
}

public readonly record struct MonitorTargetRequest(
    NativeRect? ForegroundRect,
    bool UseCursorMonitor,
    Point CursorPosition,
    string? LastMonitorDeviceName);

public static class MonitorTargeting
{
    private const uint MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<MonitorSnapshot> CaptureScreens()
    {
        var monitors = new List<MonitorSnapshot>();
        var callbackError = 0;
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx
            {
                Size = Marshal.SizeOf<MonitorInfoEx>()
            };

            if (!GetMonitorInfo(monitor, ref info))
            {
                callbackError = Marshal.GetLastWin32Error();
                return false;
            }

            var dpiScale = GetDpiScale(monitor);
            monitors.Add(new MonitorSnapshot(
                info.DeviceName ?? string.Empty,
                info.Bounds,
                info.WorkingArea,
                (info.Flags & MonitorInfoPrimary) != 0,
                dpiScale.X,
                dpiScale.Y));
            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new Win32Exception(callbackError != 0 ? callbackError : Marshal.GetLastWin32Error());
        }

        return monitors;
    }

    public static Point GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : Point.Empty;
    }

    public static MonitorSnapshot SelectMonitor(
        IReadOnlyList<MonitorSnapshot> monitors,
        MonitorTargetRequest request)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        if (request.ForegroundRect is NativeRect foregroundRect)
        {
            var center = new Point(
                foregroundRect.Left + foregroundRect.Width / 2,
                foregroundRect.Top + foregroundRect.Height / 2);
            var centerMonitor = FirstContaining(monitors, center);
            if (centerMonitor is not null)
            {
                return centerMonitor.Value;
            }

            var overlapMonitor = LargestOverlap(monitors, foregroundRect);
            if (overlapMonitor is not null)
            {
                return overlapMonitor.Value;
            }
        }

        if (request.UseCursorMonitor)
        {
            var cursorMonitor = FirstContaining(monitors, request.CursorPosition);
            if (cursorMonitor is not null)
            {
                return cursorMonitor.Value;
            }
        }

        return FindByDeviceName(monitors, request.LastMonitorDeviceName) ?? PrimaryOrFirst(monitors);
    }

    private static MonitorSnapshot? FirstContaining(IReadOnlyList<MonitorSnapshot> monitors, Point point)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Contains(point))
            {
                return monitor;
            }
        }

        return null;
    }

    private static MonitorSnapshot? LargestOverlap(IReadOnlyList<MonitorSnapshot> monitors, NativeRect rect)
    {
        MonitorSnapshot? selected = null;
        var selectedArea = 0L;
        foreach (var monitor in monitors)
        {
            var area = IntersectionArea(monitor.Bounds, rect);
            if (area > selectedArea)
            {
                selected = monitor;
                selectedArea = area;
            }
        }

        return selectedArea == 0 ? null : selected;
    }

    private static MonitorSnapshot? FindByDeviceName(
        IReadOnlyList<MonitorSnapshot> monitors,
        string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    private static MonitorSnapshot PrimaryOrFirst(IReadOnlyList<MonitorSnapshot> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }

        return monitors[0];
    }

    private static long IntersectionArea(NativeRect first, NativeRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (long)(right - left) * (bottom - top);
    }

    private static (double X, double Y) GetDpiScale(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return (1, 1);
        }

        var dpiScale = GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out var dpiY) == 0
            ? Math.Max(dpiX / 96d, dpiY / 96d)
            : 1;
        var factorScale = GetScaleFactorForMonitor(monitor, out var scaleFactor) == 0
            ? scaleFactor / 100d
            : 1;
        var scale = Math.Max(dpiScale, factorScale);
        return (scale, scale);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("shcore.dll")]
    private static extern int GetScaleFactorForMonitor(
        IntPtr monitor,
        out int scaleFactor);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Bounds;
        public NativeRect WorkingArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRect,
        IntPtr data);
}
