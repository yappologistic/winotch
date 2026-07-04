using System.Drawing;
using System.Runtime.InteropServices;
using FormsScreen = System.Windows.Forms.Screen;

namespace Winotch;

public readonly record struct MonitorSnapshot(
    string DeviceName,
    NativeRect Bounds,
    NativeRect WorkingArea,
    bool IsPrimary,
    double DpiScaleX,
    double DpiScaleY)
{
    public double LeftDip => Bounds.Left;
    public double TopDip => Bounds.Top;
    public double WidthDip => Bounds.Width;
    public double WorkAreaLeftDip => WorkingArea.Left;
    public double WorkAreaTopDip => WorkingArea.Top;
    public double WorkAreaRightDip => WorkingArea.Right;
    public double WorkAreaBottomDip => WorkingArea.Bottom;

    public bool Contains(Point point) =>
        point.X >= Bounds.Left &&
        point.X < Bounds.Right &&
        point.Y >= Bounds.Top &&
        point.Y < Bounds.Bottom;

}

public readonly record struct MonitorTargetRequest(
    NativeRect? ForegroundRect,
    bool UseCursorMonitor,
    Point CursorPosition,
    string? LastMonitorDeviceName);

public static class MonitorTargeting
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static IReadOnlyList<MonitorSnapshot> CaptureScreens() =>
        FormsScreen.AllScreens.Select(FromScreen).ToArray();

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

    public static MonitorSnapshot FromScreen(FormsScreen screen)
    {
        var monitor = MonitorFromPoint(
            new NativePoint(
                screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2),
            MonitorDefaultToNearest);
        var dpiScale = GetDpiScale(monitor);
        return new MonitorSnapshot(
            screen.DeviceName,
            ToNativeRect(screen.Bounds),
            ToNativeRect(screen.WorkingArea),
            screen.Primary,
            dpiScale.X,
            dpiScale.Y);
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

    private static NativeRect ToNativeRect(Rectangle rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static (double X, double Y) GetDpiScale(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero ||
            GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out var dpiY) != 0)
        {
            return (1, 1);
        }

        return (dpiX / 96d, dpiY / 96d);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
