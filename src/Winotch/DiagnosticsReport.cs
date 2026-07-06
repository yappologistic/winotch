using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications.Management;
using Forms = System.Windows.Forms;

namespace Winotch;

public static class DiagnosticsReport
{
    private const string NotificationListenerType = "Windows.UI.Notifications.Management.UserNotificationListener";

    public static async Task<string> CaptureAsync(WinotchSettings settings, StartupService startup)
    {
        var executablePath = StartupService.CurrentExecutablePath();
        var snapshot = new DiagnosticsSnapshot
        {
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev",
            ExecutablePath = executablePath,
            SettingsPath = SettingsService.DefaultPath,
            OsVersion = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Settings = settings.Normalize(),
            Startup = startup.GetState(executablePath),
            NotificationHistoryAccess = ReadNotificationAccess(),
            BatteryStatus = ReadBatteryStatus(),
            AudioOutputCount = ReadAudioOutputCount(),
            CameraDeviceCount = await ReadCameraDeviceCountAsync(),
            Monitors = CaptureMonitors()
        };

        return Build(snapshot);
    }

    public static string Build(DiagnosticsSnapshot snapshot)
    {
        var settings = snapshot.Settings.Normalize();
        var builder = new StringBuilder()
            .AppendLine("Winotch Diagnostics")
            .AppendLine()
            .AppendLine("App")
            .AppendLine(CultureInfo.InvariantCulture, $"- Version: {snapshot.AppVersion}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Executable: {RedactPath(snapshot.ExecutablePath)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Settings path: {RedactPath(snapshot.SettingsPath)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- OS: {snapshot.OsVersion}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Runtime: {snapshot.FrameworkDescription}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Process architecture: {snapshot.ProcessArchitecture}")
            .AppendLine()
            .AppendLine("Settings")
            .AppendLine(CultureInfo.InvariantCulture, $"- 24-hour clock: {State(settings.General.Use24HourClock)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Show date: {State(settings.General.ShowDate)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Start with Windows: {State(settings.General.StartWithWindows)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Clipboard capture: {State(settings.Features.ClipboardHistoryEnabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Per-app mixer: {State(settings.Features.ShowAppMixer)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- System stats: {State(settings.Features.SystemStatsEnabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Follow active monitor: {State(settings.Features.FollowActiveMonitor)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Media toasts: {State(settings.Toasts.MediaToastsEnabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Notification toasts: {State(settings.Toasts.NotificationToastsEnabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Priority alerts: {State(settings.Toasts.PriorityAlertsEnabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Toast duration: {settings.Toasts.DurationScale}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Calendar enabled: {State(settings.Calendar.Enabled)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Calendar subscriptions: {settings.Calendar.SubscriptionUrls.Count}")
            .AppendLine()
            .AppendLine("Windows")
            .AppendLine(CultureInfo.InvariantCulture, $"- Startup registration: {StartupState(snapshot.Startup)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Notification history: {snapshot.NotificationHistoryAccess}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Battery: {snapshot.BatteryStatus}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Audio outputs: {CountOrUnknown(snapshot.AudioOutputCount)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Camera devices: {CountOrUnknown(snapshot.CameraDeviceCount)}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Monitors: {snapshot.Monitors.Count}");

        for (var index = 0; index < snapshot.Monitors.Count; index++)
        {
            var monitor = snapshot.Monitors[index];
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- Display {index + 1}: {(monitor.IsPrimary ? "primary" : "secondary")}, " +
                $"bounds={RectText(monitor.Bounds)}, work={RectText(monitor.WorkingArea)}, " +
                $"dpi={monitor.DpiScaleX:0.##}x{monitor.DpiScaleY:0.##}");
        }

        builder.AppendLine()
            .AppendLine("Privacy")
            .AppendLine("- Does not include clipboard contents, notification text, calendar URLs, Wi-Fi names, camera frames, or audio device names.");

        return builder.ToString();
    }

    private static string State(bool enabled) => enabled ? "enabled" : "disabled";

    private static string CountOrUnknown(int? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string StartupState(StartupState state) =>
        !state.CanAccess ? "unavailable" : state.IsEnabled ? "enabled" : "disabled";

    private static string RectText(NativeRect rect) =>
        $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}";

    private static string RedactPath(string path)
    {
        var redacted = path;
        foreach (var folder in KnownFolders())
        {
            if (redacted.StartsWith(folder.Path, StringComparison.OrdinalIgnoreCase))
            {
                redacted = folder.Token + redacted[folder.Path.Length..];
            }
        }

        return redacted;
    }

    private static IEnumerable<(string Path, string Token)> KnownFolders()
    {
        var folders = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        };

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Item1))
            .OrderByDescending(folder => folder.Item1.Length)
            .Select(folder => (folder.Item1, folder.Item2));
    }

    private static IReadOnlyList<MonitorSnapshot> CaptureMonitors()
    {
        try
        {
            return MonitorTargeting.CaptureScreens();
        }
        catch
        {
            return [];
        }
    }

    private static string ReadNotificationAccess()
    {
        try
        {
            return ApiInformation.IsTypePresent(NotificationListenerType)
                ? UserNotificationListener.Current.GetAccessStatus().ToString()
                : "Unavailable";
        }
        catch
        {
            return "Unavailable";
        }
    }

    private static string ReadBatteryStatus()
    {
        try
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
            {
                return "No system battery";
            }

            var percent = power.BatteryLifePercent >= 0
                ? $"{Math.Round(power.BatteryLifePercent * 100):0}%"
                : "unknown percent";
            var charging = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Charging)
                ? "charging"
                : "discharging";
            return $"{percent}, {charging}, {power.PowerLineStatus}";
        }
        catch
        {
            return "Unavailable";
        }
    }

    private static int? ReadAudioOutputCount()
    {
        try
        {
            return new AudioDeviceService().GetRenderDevices().Count;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int?> ReadCameraDeviceCountAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask();
            return devices.Count;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record DiagnosticsSnapshot
{
    public string AppVersion { get; init; } = "dev";
    public string ExecutablePath { get; init; } = "";
    public string SettingsPath { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string FrameworkDescription { get; init; } = "";
    public string ProcessArchitecture { get; init; } = "";
    public WinotchSettings Settings { get; init; } = WinotchSettings.Defaults;
    public StartupState Startup { get; init; } = new(IsEnabled: false, CanAccess: false, ErrorMessage: null);
    public string NotificationHistoryAccess { get; init; } = "Unavailable";
    public string BatteryStatus { get; init; } = "Unavailable";
    public int? AudioOutputCount { get; init; }
    public int? CameraDeviceCount { get; init; }
    public IReadOnlyList<MonitorSnapshot> Monitors { get; init; } = [];
}
