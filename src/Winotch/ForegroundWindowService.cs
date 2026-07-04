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

        var mode = DecideMode(isOwnWindow, IsShellClass(className), isMaximized: false, windowRect, default, default);
        return new ForegroundWindowSnapshot(mode, windowRect, UseCursorMonitor: false);
    }

    public static ShellMode DecideMode(
        bool isOwnWindow,
        bool isShell,
        bool isMaximized,
        NativeRect windowRect,
        NativeRect monitorRect,
        NativeRect workAreaRect) => ShellMode.Mini;

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
