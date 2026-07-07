using System.Runtime.InteropServices;
using System.Text;

namespace Winotch.CommandBar;

public sealed class WindowSwitchProvider : ICommandProvider
{
    public string Name => "Windows";
    public int Priority => 90;

    public bool IsEnabled(CommandBarSettings settings) => settings.WindowSwitcherEnabled;

    public Task<IReadOnlyList<CommandBarResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = WindowEnumerator.OpenWindows()
            .Select(window => (window, score: CommandMatch.Score(query, window.Title)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .Take(6)
            .Select(match => new CommandBarResult(
                match.window.Title,
                "Switch window",
                Name,
                CommandMatch.Rank(match.score, Priority),
                Priority,
                _ =>
                {
                    WindowEnumerator.Activate(match.window.Handle);
                    return Task.CompletedTask;
                },
                null,
                "\uE8A7"))
            .ToList();
        return Task.FromResult<IReadOnlyList<CommandBarResult>>(results);
    }
}

internal sealed record OpenWindow(IntPtr Handle, string Title);

internal static class WindowEnumerator
{
    public static IReadOnlyList<OpenWindow> OpenWindows()
    {
        var windows = new List<OpenWindow>();
        EnumWindows((candidate, _) =>
        {
            var title = GetTitle(candidate);
            if (string.IsNullOrWhiteSpace(title) || !GetWindowRect(candidate, out var rect))
            {
                return true;
            }

            if (ForegroundWindowService.IsCandidateAppWindow(
                IsWindowVisible(candidate),
                IsOwnWindow(candidate),
                ForegroundWindowService.IsShellClass(GetClassName(candidate)),
                IsIconic(candidate),
                IsCloaked(candidate),
                rect))
            {
                windows.Add(new OpenWindow(candidate, title));
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static void Activate(IntPtr handle)
    {
        if (IsIconic(handle))
        {
            ShowWindow(handle, 9);
        }

        SetForegroundWindow(handle);
    }

    private static bool IsOwnWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    private static bool IsCloaked(IntPtr window) =>
        DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string GetTitle(IntPtr window)
    {
        var builder = new StringBuilder(256);
        var length = GetWindowText(window, builder, builder.Capacity);
        return length == 0 ? "" : builder.ToString();
    }

    private static string GetClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(window, builder, builder.Capacity);
        return length == 0 ? "" : builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}

