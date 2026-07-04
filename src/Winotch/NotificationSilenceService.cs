using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Winotch;

public enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningDirect3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

public static class NotificationSilenceService
{
    private const string SettingsKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    public static bool IsSilenced()
    {
        try
        {
            var value = Registry.GetValue(SettingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", null);
            return IsGloballySilenced(value as int?) ||
                (TryGetShellNotificationState(out var state) && IsShellNotificationSuppressed(state));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGloballySilenced(int? globalToastsEnabled) => globalToastsEnabled == 0;

    public static bool IsShellNotificationSuppressed(UserNotificationState state) =>
        state == UserNotificationState.QuietTime;

    private static bool TryGetShellNotificationState(out UserNotificationState state) =>
        SHQueryUserNotificationState(out state) == 0;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);
}
