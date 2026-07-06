namespace Winotch.Tests;

public class DiagnosticsReportTests
{
    [Fact]
    public void BuildIncludesDeviceDebugFields()
    {
        var snapshot = SampleSnapshot() with
        {
            BatteryStatus = "83%, charging",
            AudioOutputCount = 2,
            CameraDeviceCount = 1,
            NotificationHistoryAccess = "Denied"
        };

        var report = DiagnosticsReport.Build(snapshot);

        Assert.Contains("Winotch Diagnostics", report);
        Assert.Contains("OS: Windows 11", report);
        Assert.Contains("Runtime: .NET 8", report);
        Assert.Contains("Process architecture: X64", report);
        Assert.Contains("Notification history: Denied", report);
        Assert.Contains("Battery: 83%, charging", report);
        Assert.Contains("Audio outputs: 2", report);
        Assert.Contains("Camera devices: 1", report);
        Assert.Contains("Monitors: 1", report);
        Assert.Contains("Display 1: primary, bounds=0,0 1920x1080, work=0,0 1920x1040, dpi=1.5x1.5", report);
    }

    [Fact]
    public void BuildOmitsPrivateContentAndRawUserProfilePaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var snapshot = SampleSnapshot() with
        {
            ExecutablePath = Path.Combine(userProfile, "Winotch", "Winotch.exe"),
            SettingsPath = SettingsService.DefaultPath,
            Settings = WinotchSettings.Defaults with
            {
                Calendar = new CalendarSettings
                {
                    Enabled = true,
                    SubscriptionUrls =
                    [
                        "https://calendar.example/private-token.ics"
                    ]
                }
            }
        };

        var report = DiagnosticsReport.Build(snapshot);

        Assert.Contains("Calendar subscriptions: 1", report);
        Assert.Contains("%USERPROFILE%", report);
        Assert.Contains("%LOCALAPPDATA%", report);
        Assert.DoesNotContain("private-token", report);
        Assert.DoesNotContain("calendar.example", report);
        Assert.DoesNotContain("notification body", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userProfile, report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Does not include clipboard contents, notification text, calendar URLs", report);
    }

    [Fact]
    public async Task CaptureAsyncReturnsReviewableReport()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"));
        var startup = new StartupService(new EmptyRunKeyStore());

        var report = await DiagnosticsReport.CaptureAsync(settings.Current, startup);

        Assert.StartsWith("Winotch Diagnostics", report);
        Assert.Contains("Privacy", report);
        Assert.Contains("Monitors:", report);
    }

    private static DiagnosticsSnapshot SampleSnapshot() => new()
    {
        AppVersion = "1.2.3",
        ExecutablePath = @"C:\Winotch\Winotch.exe",
        SettingsPath = @"C:\Users\tester\AppData\Local\Winotch\settings.json",
        OsVersion = "Windows 11",
        FrameworkDescription = ".NET 8",
        ProcessArchitecture = "X64",
        Settings = WinotchSettings.Defaults,
        Startup = new StartupState(IsEnabled: true, CanAccess: true, ErrorMessage: null),
        NotificationHistoryAccess = "Allowed",
        BatteryStatus = "No system battery",
        AudioOutputCount = null,
        CameraDeviceCount = null,
        Monitors =
        [
            new MonitorSnapshot(
                "DISPLAY1",
                new NativeRect(0, 0, 1920, 1080),
                new NativeRect(0, 0, 1920, 1040),
                IsPrimary: true,
                DpiScaleX: 1.5,
                DpiScaleY: 1.5)
        ]
    };

    private sealed class EmptyRunKeyStore : IRunKeyStore
    {
        public string? Read(string name) => null;
        public void Write(string name, string value) { }
        public void Delete(string name) { }
    }
}
