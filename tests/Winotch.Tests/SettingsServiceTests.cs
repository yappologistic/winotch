namespace Winotch.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void SettingsServiceLoadsDefaultsWhenFileIsMissing()
    {
        using var temp = new TempSettingsDirectory();

        var service = new SettingsService(temp.SettingsPath);

        Assert.False(service.Current.General.Use24HourClock);
        Assert.True(service.Current.General.ShowDate);
        Assert.True(service.Current.Toasts.MediaToastsEnabled);
        Assert.Equal(ToastDurationScale.Normal, service.Current.Toasts.DurationScale);
    }

    [Fact]
    public void SettingsServicePersistsIndentedRoundtripJson()
    {
        using var temp = new TempSettingsDirectory();
        var service = new SettingsService(temp.SettingsPath);

        service.Update(settings => settings with
        {
            General = settings.General with { Use24HourClock = true, ShowDate = false },
            Toasts = settings.Toasts with
            {
                NotificationToastsEnabled = false,
                DurationScale = ToastDurationScale.Long
            }
        });

        var reloaded = new SettingsService(temp.SettingsPath);
        Assert.True(reloaded.Current.General.Use24HourClock);
        Assert.False(reloaded.Current.General.ShowDate);
        Assert.False(reloaded.Current.Toasts.NotificationToastsEnabled);
        Assert.Equal(ToastDurationScale.Long, reloaded.Current.Toasts.DurationScale);
        Assert.Contains(Environment.NewLine, File.ReadAllText(temp.SettingsPath));
    }

    [Fact]
    public void SettingsServiceRenamesCorruptJsonAndFallsBackToDefaults()
    {
        using var temp = new TempSettingsDirectory();
        File.WriteAllText(temp.SettingsPath, "{ not json");

        var service = new SettingsService(temp.SettingsPath);

        Assert.True(service.Current.General.ShowDate);
        Assert.False(File.Exists(temp.SettingsPath));
        Assert.True(File.Exists(SettingsService.BadPathFor(temp.SettingsPath)));
    }

    [Fact]
    public void SettingsServiceFallsBackToDefaultsWhenFileIsLocked()
    {
        using var temp = new TempSettingsDirectory();
        File.WriteAllText(temp.SettingsPath, "{}");

        using var locked = new FileStream(temp.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var service = new SettingsService(temp.SettingsPath);

        Assert.True(service.Current.General.ShowDate);
    }

    [Fact]
    public void SettingsServiceFiresChangeEventOnlyWhenValueChanges()
    {
        using var temp = new TempSettingsDirectory();
        var service = new SettingsService(temp.SettingsPath);
        var count = 0;
        WinotchSettings? last = null;
        service.Changed += (_, args) =>
        {
            count++;
            last = args;
        };

        service.Update(settings => settings);
        service.Update(settings => settings with
        {
            General = settings.General with { Use24HourClock = true }
        });

        Assert.Equal(1, count);
        Assert.True(last?.General.Use24HourClock);
    }

    [Fact]
    public void SettingsServiceConcurrentSavesLeaveReadableJson()
    {
        using var temp = new TempSettingsDirectory();
        var service = new SettingsService(temp.SettingsPath);

        Parallel.For(0, 24, index =>
        {
            service.Update(settings => settings with
            {
                General = settings.General with { Use24HourClock = index % 2 == 0 }
            });
        });

        var reloaded = new SettingsService(temp.SettingsPath);
        Assert.NotNull(reloaded.Current.General);
    }

    [Theory]
    [InlineData(ToastDurationScale.Short, 2850)]
    [InlineData(ToastDurationScale.Normal, 3800)]
    [InlineData(ToastDurationScale.Long, 5700)]
    public void ToastDurationScaleMapsToExpectedDurations(ToastDurationScale scale, int expectedMilliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), scale.ApplyTo(ShellAnimationTiming.MediaToastDuration));
    }

    [Fact]
    public void StartupRunValueQuotesExecutablePathsWithSpaces()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Winotch\\Winotch.exe\"",
            StartupService.FormatRunValue(@"C:\Program Files\Winotch\Winotch.exe"));
    }

    [Fact]
    public void StartupServiceRewritesStaleRunKeyPath()
    {
        var store = new FakeRunKeyStore
        {
            Value = StartupService.FormatRunValue(@"C:\Old Path\Winotch.exe")
        };
        var service = new StartupService(store);

        var state = service.GetState(@"C:\Program Files\Winotch\Winotch.exe");

        Assert.True(state.IsEnabled);
        Assert.True(state.CanAccess);
        Assert.Equal(
            "\"C:\\Program Files\\Winotch\\Winotch.exe\"",
            store.Value);
    }

    [Fact]
    public void StartupServiceReadsCurrentRunKeyPath()
    {
        var store = new FakeRunKeyStore
        {
            Value = StartupService.FormatRunValue(@"C:\Program Files\Winotch\Winotch.exe")
        };
        var service = new StartupService(store);

        var state = service.GetState(@"C:\Program Files\Winotch\Winotch.exe");

        Assert.True(state.IsEnabled);
        Assert.True(state.CanAccess);
    }

    private sealed class TempSettingsDirectory : IDisposable
    {
        public TempSettingsDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Winotch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        }

        public string DirectoryPath { get; }
        public string SettingsPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FakeRunKeyStore : IRunKeyStore
    {
        public string? Value { get; set; }

        public string? Read(string name) => Value;

        public void Write(string name, string value)
        {
            Value = value;
        }

        public void Delete(string name)
        {
            Value = null;
        }
    }
}
