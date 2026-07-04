using System.Runtime.InteropServices;
using System.Text;

namespace Winotch;

public static class ForegroundWindowService
{
    private const int DwmWindowAttributeCloaked = 14;

    public static ShellMode DetectShellMode() => DetectForeground().Mode;

    public static ForegroundWindowSnapshot DetectForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, WindowRect: null, UseCursorMonitor: false);
        }

        var candidate = foreground;
        var isOwnWindow = IsOwnWindow(candidate);
        var className = GetClassName(candidate);
        if (IsShellClass(className))
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, WindowRect: null, UseCursorMonitor: true);
        }

        if (isOwnWindow && !TryFindTopLevelAppWindow(out candidate))
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, WindowRect: null, UseCursorMonitor: false);
        }

        if (candidate != foreground)
        {
            isOwnWindow = IsOwnWindow(candidate);
            className = GetClassName(candidate);
        }

        if (IsIconic(candidate) || IsCloaked(candidate))
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, WindowRect: null, UseCursorMonitor: false);
        }

        if (!GetWindowRect(candidate, out var windowRect))
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, WindowRect: null, UseCursorMonitor: false);
        }

        if (!TryGetMonitorRects(candidate, out var monitorRect, out var workAreaRect))
        {
            return new ForegroundWindowSnapshot(ShellMode.Mini, windowRect, UseCursorMonitor: false);
        }

        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        var isMaximized = GetWindowPlacement(candidate, ref placement) && placement.ShowCmd == 3;
        var mode = DecideMode(isOwnWindow, IsShellClass(className), isMaximized, windowRect, monitorRect, workAreaRect);
        return new ForegroundWindowSnapshot(mode, windowRect, UseCursorMonitor: false);
    }

    public static ShellMode DecideMode(
        bool isOwnWindow,
        bool isShell,
        bool isMaximized,
        NativeRect windowRect,
        NativeRect monitorRect,
        NativeRect workAreaRect)
    {
        if (isOwnWindow || isShell)
        {
            return ShellMode.Mini;
        }

        var widthCoverage = (double)windowRect.Width / Math.Max(1, monitorRect.Width);
        var screenHeightCoverage = (double)windowRect.Height / Math.Max(1, monitorRect.Height);
        var workAreaHeightCoverage = (double)windowRect.Height / Math.Max(1, workAreaRect.Height);
        var coversTop = windowRect.Top <= monitorRect.Top + 8;
        var fillsWorkAreaTop = Math.Abs(windowRect.Top - workAreaRect.Top) <= 8;
        var fillsScreen = widthCoverage >= 0.9 && screenHeightCoverage >= 0.78 && coversTop;
        var fillsWorkArea = widthCoverage >= 0.9 && workAreaHeightCoverage >= 0.78 && fillsWorkAreaTop;

        return isMaximized || fillsScreen || fillsWorkArea ? ShellMode.FullBar : ShellMode.Mini;
    }

    public static bool IsCandidateAppWindow(
        bool isVisible,
        bool isOwnWindow,
        bool isShell,
        bool isMinimized,
        bool isCloaked,
        NativeRect rect) =>
        isVisible &&
        !isOwnWindow &&
        !isShell &&
        !isMinimized &&
        !isCloaked &&
        rect.Width > 160 &&
        rect.Height > 120;

    private static bool TryGetMonitorRects(IntPtr window, out NativeRect monitorRect, out NativeRect workAreaRect)
    {
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            monitorRect = default;
            workAreaRect = default;
            return false;
        }

        monitorRect = info.Monitor;
        workAreaRect = info.WorkArea;
        return true;
    }

    private static bool TryFindTopLevelAppWindow(out IntPtr window)
    {
        var result = IntPtr.Zero;
        EnumWindows((candidate, _) =>
        {
            if (!IsAppWindow(candidate))
            {
                return true;
            }

            result = candidate;
            return false;
        }, IntPtr.Zero);
        window = result;
        return window != IntPtr.Zero;
    }

    private static bool IsAppWindow(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect))
        {
            return false;
        }

        return IsCandidateAppWindow(
            IsWindowVisible(window),
            IsOwnWindow(window),
            IsShellClass(GetClassName(window)),
            IsIconic(window),
            IsCloaked(window),
            rect);
    }

    private static bool IsOwnWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    public static bool IsShellClass(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmWindowAttributeCloaked, out var cloaked, sizeof(int)) == 0 &&
        cloaked != 0;

    private static string GetClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(window, builder, builder.Capacity);
        return length == 0 ? "" : builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}

public readonly record struct ForegroundWindowSnapshot(
    ShellMode Mode,
    NativeRect? WindowRect,
    bool UseCursorMonitor);

[StructLayout(LayoutKind.Sequential)]
public struct NativeRect
{
    public NativeRect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public int Size;
    public NativeRect Monitor;
    public NativeRect WorkArea;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public int Length;
    public int Flags;
    public int ShowCmd;
    public System.Drawing.Point MinPosition;
    public System.Drawing.Point MaxPosition;
    public NativeRect NormalPosition;
}
