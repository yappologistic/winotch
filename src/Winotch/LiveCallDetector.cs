using System.Diagnostics;

namespace Winotch;

public sealed class LiveCallDetector
{
    private readonly Func<IReadOnlyList<LiveCallWindow>> _readWindows;

    public LiveCallDetector(Func<IReadOnlyList<LiveCallWindow>> readWindows)
    {
        _readWindows = readWindows;
    }

    public static LiveCallDetector CreateDefault() => new(ReadProcessWindows);

    public LiveCallSnapshot Detect() =>
        new(_readWindows().Any(IsLikelyActiveCall));

    public static bool IsLikelyActiveCall(LiveCallWindow window)
    {
        var process = window.ProcessName.Trim().ToLowerInvariant();
        var title = window.Title.Trim().ToLowerInvariant();
        if (process.Length == 0 || title.Length == 0)
        {
            return false;
        }

        var isMeetingApp = process.Contains("teams", StringComparison.Ordinal) ||
            process.Contains("zoom", StringComparison.Ordinal);
        var isBrowserMeet = IsBrowser(process) && title.Contains("meet", StringComparison.Ordinal);
        var hasCallTitle = title.Contains("meeting", StringComparison.Ordinal) ||
            title.Contains("call", StringComparison.Ordinal) ||
            title.Contains("google meet", StringComparison.Ordinal) ||
            title.Contains("zoom meeting", StringComparison.Ordinal);

        // Calendar/chat/home views mention the same apps but are not active calls.
        var looksIdle = title is "calendar" or "chat" or "activity" ||
            title.Contains("settings", StringComparison.Ordinal);
        return !looksIdle && (isMeetingApp || isBrowserMeet) && hasCallTitle;
    }

    private static bool IsBrowser(string process) =>
        process is "chrome" or "msedge" or "firefox" or "brave" or "opera";

    private static IReadOnlyList<LiveCallWindow> ReadProcessWindows()
    {
        var windows = new List<LiveCallWindow>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    windows.Add(new LiveCallWindow(process.ProcessName, process.MainWindowTitle));
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return windows;
    }
}
