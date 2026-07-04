using System.Windows.Media;
using Windows.Storage;

namespace Winotch.Tests;

public class StatusShellTests
{
    [Fact]
    public void NetshParserReadsConnectedWifiAndSignal()
    {
        var output = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                State                  : connected
                SSID                   : TELUS1255
                BSSID                  : 00:11:22:33:44:55
                Signal                 : 99%
            """;

        var status = WifiService.ParseCurrentNetsh(output);

        Assert.Equal("TELUS1255", status.Name);
        Assert.Equal("99%", status.Signal);
    }

    [Fact]
    public void ProfileParserTrimsWindowsProfileIndex()
    {
        var status = WifiService.ParseCurrentProfile("TELUS1255 2\r\n");

        Assert.Equal("TELUS1255", status);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   \r\n\t", null)]
    [InlineData("Coffee Shop 12\r\n", "Coffee Shop")]
    [InlineData("TELUS1255\r\n", "TELUS1255")]
    public void ProfileParserHandlesEmptyAndIndexedNames(string output, string? expected)
    {
        Assert.Equal(expected, WifiService.ParseCurrentProfile(output));
    }

    [Fact]
    public void NetshParserIgnoresBssidWhenNoConnectedSsidExists()
    {
        var output = """
            There is 1 interface on the system:

                Name                   : Wi-Fi
                State                  : disconnected
                BSSID                  : 00:11:22:33:44:55
            """;

        var status = WifiService.ParseCurrentNetsh(output);

        Assert.Null(status.Name);
        Assert.Null(status.Signal);
    }

    [Fact]
    public void NetworkParserDeduplicatesScannedNetworks()
    {
        var output = """
            SSID 1 : TELUS1255
                Network type            : Infrastructure
                Signal                  : 96%
            SSID 2 : TELUS1255
                Network type            : Infrastructure
                Signal                  : 75%
            SSID 3 : Guest
                Network type            : Infrastructure
                Signal                  : 41%
            """;

        var networks = WifiService.ParseNetworks(output);

        Assert.Collection(
            networks,
            first => Assert.Equal("TELUS1255", first.Name),
            second => Assert.Equal("Guest", second.Name));
    }

    [Fact]
    public void NetworkParserSkipsBlankNamesAndCapsTheVisibleList()
    {
        var output = string.Join(
            Environment.NewLine,
            new[]
            {
                "SSID 1 :    ",
                "    Signal                  : 12%",
                "SSID 2 : Network-1",
                "    Signal                  : 91%",
                "SSID 3 : Network-2",
                "    Signal                  : 82%",
                "SSID 4 : Network-3",
                "    Signal                  : 73%",
                "SSID 5 : Network-4",
                "    Signal                  : 64%",
                "SSID 6 : Network-5",
                "    Signal                  : 55%",
                "SSID 7 : Network-6",
                "    Signal                  : 46%",
                "SSID 8 : Network-7",
                "    Signal                  : 37%",
                "SSID 9 : Network-8",
                "    Signal                  : 28%",
                "SSID 10 : Network-9",
                "    Signal                  : 19%"
            });

        var networks = WifiService.ParseNetworks(output);

        Assert.Equal(8, networks.Count);
        Assert.Equal("Network-1", networks[0].Name);
        Assert.Equal("91%", networks[0].Signal);
        Assert.Equal("Network-8", networks[^1].Name);
        Assert.DoesNotContain(networks, network => string.IsNullOrWhiteSpace(network.Name));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("99%", "99%")]
    public void WifiStatusOmitsBlankSignalText(string? signal, string expected)
    {
        Assert.Equal(expected, new WifiStatus("TELUS1255", signal).SignalText);
    }

    [Theory]
    [InlineData("", "TELUS1255")]
    [InlineData("   ", "TELUS1255")]
    [InlineData("99%", "TELUS1255 (99%)")]
    public void WifiNetworkDisplayTextIncludesSignalOnlyWhenPresent(string signal, string expected)
    {
        Assert.Equal(expected, new WifiNetwork("TELUS1255", signal).ToString());
    }

    [Theory]
    [InlineData(96, false, 15.36, 246, 246, 244)]
    [InlineData(96, true, 15.36, 50, 215, 75)]
    [InlineData(49, false, 7.84, 255, 204, 0)]
    [InlineData(19, false, 3.04, 255, 69, 58)]
    public void BatteryVisualUsesFillAndThresholdColors(int percent, bool isCharging, double expectedWidth, byte red, byte green, byte blue)
    {
        var visual = BatteryVisual.FromPercent(percent, isCharging);
        var brush = Assert.IsType<SolidColorBrush>(visual.Brush);

        Assert.Equal(expectedWidth, visual.FillWidth, precision: 2);
        Assert.Equal(Color.FromRgb(red, green, blue), brush.Color);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(100, 16)]
    [InlineData(200, 16)]
    public void BatteryVisualClampsFillToIconInterior(int percent, double expectedWidth)
    {
        var visual = BatteryVisual.FromPercent(percent);

        Assert.Equal(expectedWidth, visual.FillWidth, precision: 2);
        Assert.Equal(expectedWidth, BatteryVisual.FillWidthForPercent(percent), precision: 2);
    }

    [Theory]
    [InlineData(-10, 24, 0)]
    [InlineData(5, 24, 1.2)]
    [InlineData(50, 24, 12)]
    [InlineData(100, 24, 24)]
    [InlineData(200, 24, 24)]
    [InlineData(50, -1, 0)]
    public void BatteryVisualCalculatesFillWidthForAnyGlyphInterior(int percent, double maxWidth, double expectedWidth)
    {
        Assert.Equal(expectedWidth, BatteryVisual.FillWidthForPercent(percent, maxWidth), precision: 2);
    }

    [Theory]
    [InlineData(61, 60, 24, 14.4, 14.64)]
    [InlineData(5, -10, 24, 0, 1.2)]
    [InlineData(100, 100, 24, 24, 24)]
    [InlineData(200, null, 24, 0, 24)]
    public void BatteryVisualDerivesChargingFillAnimation(int percent, int? previousPercent, double maxWidth, double expectedFrom, double expectedTo)
    {
        var animation = BatteryVisual.ChargingFillAnimation(percent, previousPercent, maxWidth);

        Assert.Equal(expectedFrom, animation.FromWidth, precision: 2);
        Assert.Equal(expectedTo, animation.ToWidth, precision: 2);
        Assert.Equal(ShellAnimationTiming.ChargingFillDuration, animation.Duration);
        Assert.Equal(ShellAnimationTiming.ChargingTintSweepDuration, animation.SweepDuration);
    }

    [Theory]
    [InlineData(50, 246, 246, 244)]
    [InlineData(49, 255, 204, 0)]
    [InlineData(20, 255, 204, 0)]
    [InlineData(19, 255, 69, 58)]
    [InlineData(5, 50, 215, 75)]
    public void BatteryVisualUsesExpectedThresholdColors(int percent, byte red, byte green, byte blue)
    {
        var visual = BatteryVisual.FromPercent(percent, isCharging: percent == 5);
        var brush = Assert.IsType<SolidColorBrush>(visual.Brush);

        Assert.Equal(Color.FromRgb(red, green, blue), brush.Color);
    }

    [Fact]
    public void ForegroundModeUsesMiniForDesktopAndOwnWindow()
    {
        var monitor = new NativeRect(0, 0, 1920, 1080);
        var fullscreen = new NativeRect(0, 0, 1920, 1040);

        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: true, isShell: false, isMaximized: true, fullscreen, monitor, monitor));
        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: true, isMaximized: true, fullscreen, monitor, monitor));
    }

    [Fact]
    public void ForegroundShellClassesIncludeSecondaryTaskbar()
    {
        Assert.True(ForegroundWindowService.IsShellClass("Shell_SecondaryTrayWnd"));
    }

    [Fact]
    public void ForegroundModeUsesFullBarForMaximizedOrScreenFillingApp()
    {
        var monitor = new NativeRect(0, 0, 1920, 1080);
        var normal = new NativeRect(300, 160, 1200, 760);
        var filling = new NativeRect(0, 0, 1920, 1040);
        var reservedWorkArea = new NativeRect(0, 48, 1920, 1080);
        var fillingReservedWorkArea = new NativeRect(0, 48, 1920, 1080);

        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, normal, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: true, normal, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, filling, monitor, monitor));
        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, fillingReservedWorkArea, monitor, reservedWorkArea));
    }

    [Fact]
    public void ForegroundModeUsesMiniWhenScreenFillingThresholdsAreNotMet()
    {
        var monitor = new NativeRect(0, 0, 1000, 1000);
        var narrow = new NativeRect(0, 0, 899, 900);
        var belowHeight = new NativeRect(0, 0, 1000, 779);
        var shiftedDown = new NativeRect(0, 9, 1000, 900);

        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, narrow, monitor, monitor));
        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, belowHeight, monitor, monitor));
        Assert.Equal(ShellMode.Mini, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, shiftedDown, monitor, monitor));
    }

    [Fact]
    public void ForegroundModeUsesFullBarAtCoverageThreshold()
    {
        var monitor = new NativeRect(0, 0, 1000, 1000);
        var threshold = new NativeRect(0, 8, 900, 788);

        Assert.Equal(ShellMode.FullBar, ForegroundWindowService.DecideMode(isOwnWindow: false, isShell: false, isMaximized: false, threshold, monitor, monitor));
    }

    [Theory]
    [InlineData(true, false, false, false, false, 200, 160, true)]
    [InlineData(false, false, false, false, false, 200, 160, false)]
    [InlineData(true, true, false, false, false, 200, 160, false)]
    [InlineData(true, false, true, false, false, 200, 160, false)]
    [InlineData(true, false, false, true, false, 200, 160, false)]
    [InlineData(true, false, false, false, true, 200, 160, false)]
    [InlineData(true, false, false, false, false, 160, 160, false)]
    [InlineData(true, false, false, false, false, 200, 120, false)]
    public void CandidateAppWindowFilterRejectsShellOwnMinimizedCloakedAndTinyWindows(
        bool isVisible,
        bool isOwnWindow,
        bool isShell,
        bool isMinimized,
        bool isCloaked,
        int width,
        int height,
        bool expected)
    {
        var rect = new NativeRect(10, 20, 10 + width, 20 + height);

        Assert.Equal(expected, ForegroundWindowService.IsCandidateAppWindow(
            isVisible,
            isOwnWindow,
            isShell,
            isMinimized,
            isCloaked,
            rect));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void NotificationSilenceUsesGlobalToastToggle(int? enabled, bool expected)
    {
        Assert.Equal(expected, NotificationSilenceService.IsGloballySilenced(enabled));
    }

    [Theory]
    [InlineData(UserNotificationState.AcceptsNotifications, false)]
    [InlineData(UserNotificationState.QuietTime, true)]
    [InlineData(UserNotificationState.NotPresent, true)]
    [InlineData(UserNotificationState.Busy, true)]
    [InlineData(UserNotificationState.PresentationMode, true)]
    [InlineData(UserNotificationState.RunningDirect3DFullScreen, true)]
    [InlineData(UserNotificationState.App, false)]
    public void NotificationSilenceUsesShellNotificationState(UserNotificationState state, bool expected)
    {
        Assert.Equal(expected, NotificationSilenceService.IsShellNotificationSuppressed(state));
    }

    [Fact]
    public void NotificationServiceDisposeIsIdempotent()
    {
        var service = new NotificationService();

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void NotificationChangeTrackerPopsOnlyForNewNotifications()
    {
        var tracker = new NotificationChangeTracker();
        var first = new[] { new NotificationItem("Mail", "One", "Body") };
        var second = new[] { new NotificationItem("Mail", "Two", "Body") };

        Assert.False(tracker.ShouldPop(first));
        Assert.False(tracker.ShouldPop(first));
        Assert.True(tracker.ShouldPop(second));
        Assert.False(tracker.ShouldPop(second));
        Assert.False(tracker.ShouldPop([]));
    }

    [Fact]
    public void NotificationChangeTrackerTreatsCreationTimeAsPartOfSignature()
    {
        var first = new[]
        {
            new NotificationItem("Mail", "One", "Body", new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero), null, [])
        };
        var repeatedLater = new[]
        {
            new NotificationItem("Mail", "One", "Body", new DateTimeOffset(2026, 7, 4, 10, 1, 0, TimeSpan.Zero), null, [])
        };

        Assert.NotEqual(
            NotificationChangeTracker.CreateSignature(first),
            NotificationChangeTracker.CreateSignature(repeatedLater));
    }

    [Fact]
    public void NotificationChangeTrackerTreatsBodyAndOrderAsPartOfSignature()
    {
        var first = new[]
        {
            new NotificationItem("Mail", "One", "Body"),
            new NotificationItem("Chat", "Two", "Body")
        };
        var reordered = first.Reverse().ToArray();
        var changedBody = new[]
        {
            new NotificationItem("Mail", "One", "Different"),
            new NotificationItem("Chat", "Two", "Body")
        };

        Assert.NotEqual(
            NotificationChangeTracker.CreateSignature(first),
            NotificationChangeTracker.CreateSignature(reordered));
        Assert.NotEqual(
            NotificationChangeTracker.CreateSignature(first),
            NotificationChangeTracker.CreateSignature(changedBody));
    }

    [Fact]
    public void NotificationChangeTrackerPopsWhenNotificationsReturnAfterEmptySnapshot()
    {
        var tracker = new NotificationChangeTracker();
        var item = new[] { new NotificationItem("Mail", "One", "Body") };

        Assert.False(tracker.ShouldPop(item));
        Assert.False(tracker.ShouldPop([]));
        Assert.True(tracker.ShouldPop(item));
    }

    [Fact]
    public void NotificationItemFormatsReadableDisplayText()
    {
        var item = new NotificationItem(
            "Mail",
            "Subject",
            "Body text",
            new DateTimeOffset(2026, 7, 4, 15, 5, 0, TimeSpan.Zero),
            null,
            []);

        Assert.Equal("Mail: Subject Body text", item.ToString());
        Assert.Equal("M", item.BadgeText);
        Assert.EndsWith("M", item.TimeText);
    }

    [Fact]
    public async Task NotificationActionInvokesDelegate()
    {
        var invoked = false;
        var action = new NotificationAction("Allow", () =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await action.InvokeAsync();

        Assert.True(invoked);
    }

    [Fact]
    public void PriorityStatusTrackerSuppressesRoutineInitialState()
    {
        var tracker = new PriorityStatusTracker();

        Assert.Null(tracker.Next(Status()));
    }

    [Fact]
    public void PriorityStatusTrackerPopsLowBatteryOnceUntilRecovery()
    {
        var tracker = new PriorityStatusTracker();

        Assert.Equal("Low battery", tracker.Next(Status(percent: 19))?.Title);
        Assert.Null(tracker.Next(Status(percent: 19)));
        Assert.Null(tracker.Next(Status(percent: 18)));
        Assert.Null(tracker.Next(Status(percent: 40)));
        Assert.Equal("Low battery", tracker.Next(Status(percent: 20))?.Title);
    }

    [Fact]
    public void PriorityStatusTrackerPopsChargerTransitions()
    {
        var tracker = new PriorityStatusTracker();

        Assert.Null(tracker.Next(Status(percent: 60, charging: false)));
        Assert.Equal("Charger connected", tracker.Next(Status(percent: 61, charging: true))?.Title);
        Assert.Equal("Charger disconnected", tracker.Next(Status(percent: 62, charging: false))?.Title);
    }

    [Fact]
    public void PriorityStatusAlertMapsChargingFlourishOnlyToChargerConnect()
    {
        var connected = PriorityStatusAlert.ChargerConnected(37);
        var disconnected = PriorityStatusAlert.ChargerDisconnected(37);

        Assert.Equal(37, connected.BatteryPercent);
        Assert.True(connected.ShowsChargingFlourish);
        Assert.False(disconnected.ShowsChargingFlourish);
    }

    [Fact]
    public void PriorityStatusTrackerKeepsPendingLowBatteryBeforeChargingFlourish()
    {
        var tracker = new PriorityStatusTracker();
        var lowBattery = Status(percent: 12, microphone: true, camera: true);
        var charging = Status(percent: 12, charging: true, microphone: true, camera: true);

        Assert.Equal("Camera active", tracker.Next(lowBattery)?.Title);
        Assert.Equal("Microphone active", tracker.Next(charging)?.Title);
        Assert.Equal("Low battery", tracker.Next(charging)?.Title);

        var connected = tracker.Next(charging);

        Assert.Equal("Charger connected", connected?.Title);
        Assert.True(connected?.ShowsChargingFlourish);
    }

    [Fact]
    public void PriorityStatusTrackerPopsWifiLossAndReconnect()
    {
        var tracker = new PriorityStatusTracker();

        Assert.Null(tracker.Next(Status(wifi: "TELUS1255")));
        Assert.Equal("Wi-Fi disconnected", tracker.Next(Status(wifi: null))?.Title);
        Assert.Equal("Wi-Fi reconnected", tracker.Next(Status(wifi: "TELUS1255"))?.Title);
        Assert.Equal("Wi-Fi reconnected", tracker.Next(Status(wifi: "Guest"))?.Title);
    }

    [Fact]
    public void PriorityStatusTrackerPopsBluetoothConnectsOnly()
    {
        var tracker = new PriorityStatusTracker();

        Assert.Null(tracker.Next(Status(bluetooth: null)));
        Assert.Equal("Bluetooth connected", tracker.Next(Status(bluetooth: "Headphones"))?.Title);
        Assert.Null(tracker.Next(Status(bluetooth: "Headphones")));
        Assert.Null(tracker.Next(Status(bluetooth: null)));
    }

    [Fact]
    public void PriorityStatusTrackerQueuesInitialCriticalAlerts()
    {
        var tracker = new PriorityStatusTracker();
        var status = Status(percent: 12, microphone: true, camera: true);

        Assert.Equal("Camera active", tracker.Next(status)?.Title);
        Assert.Equal("Microphone active", tracker.Next(status)?.Title);
        Assert.Equal("Low battery", tracker.Next(status)?.Title);
        Assert.Null(tracker.Next(status));
    }

    [Theory]
    [InlineData(100L, 0L, true)]
    [InlineData(null, 0L, false)]
    [InlineData(100L, null, false)]
    [InlineData(100L, 20L, false)]
    [InlineData(0L, 0L, false)]
    public void PrivacyUseRequiresStartedAndUnstopped(long? start, long? stop, bool expected)
    {
        Assert.Equal(expected, PriorityStatusService.IsActivePrivacyUse(start, stop));
    }

    [Theory]
    [InlineData("", "", "", false, "Now playing", "Unknown artist")]
    [InlineData("", "", "MediaHost.exe", true, "Now playing", "Media Host")]
    [InlineData("Song", "", "Brave", true, "Song", "Brave")]
    [InlineData("  Song  ", "  Artist  ", "Brave", true, "Song", "Artist")]
    public void MediaSnapshotFormatsDisplayTextAndPresence(
        string title,
        string artist,
        string source,
        bool hasMedia,
        string expectedTitle,
        string expectedArtist)
    {
        var state = hasMedia ? MediaState.Playing : MediaState.None;
        var snapshot = new MediaSnapshot(title, artist, source, null, state, false, false, false, false);

        Assert.Equal(hasMedia, snapshot.HasMedia);
        Assert.Equal(expectedTitle, snapshot.DisplayTitle);
        Assert.Equal(expectedArtist, snapshot.DisplayArtist);
    }

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Apple Music")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Zune Music")]
    [InlineData("brave.exe", "brave")]
    [InlineData("", "Unknown artist")]
    public void MediaSnapshotFormatsSourceForDisplay(string source, string expected)
    {
        Assert.Equal(expected, MediaSnapshot.FormatSource(source));
    }

    [Fact]
    public void MediaChangeTrackerPopsForPlayingMediaAndSuppressesRepeats()
    {
        var tracker = new MediaChangeTracker();
        var playing = Media("Song", "Artist", MediaState.Playing);

        Assert.True(tracker.ShouldPop(playing));
        Assert.False(tracker.ShouldPop(playing));
    }

    [Fact]
    public void MediaChangeTrackerPopsForTrackChangeButNotResume()
    {
        var tracker = new MediaChangeTracker();
        var first = Media("Song", "Artist", MediaState.Playing);
        var paused = Media("Song", "Artist", MediaState.Paused);
        var next = Media("Next", "Artist", MediaState.Playing);

        Assert.True(tracker.ShouldPop(first));
        Assert.False(tracker.ShouldPop(paused));
        Assert.False(tracker.ShouldPop(first));
        Assert.True(tracker.ShouldPop(next));
    }

    [Fact]
    public void MediaChangeTrackerIgnoresEmptyOrPausedInitialSnapshots()
    {
        var tracker = new MediaChangeTracker();

        Assert.False(tracker.ShouldPop(MediaSnapshot.Empty));
        Assert.False(tracker.ShouldPop(Media("Song", "Artist", MediaState.Paused)));
        Assert.True(tracker.ShouldPop(Media("Song", "Artist", MediaState.Playing)));
    }

    [Fact]
    public void MediaArtworkReturnsNullForMissingArtwork()
    {
        Assert.Null(MediaArtwork.FromBytes(null));
        Assert.Null(MediaArtwork.FromBytes([]));
    }

    [Fact]
    public async Task AccountPictureServiceReadsNonEmptyPictureFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var bytes = await AccountPictureService.ReadFileAsync(file);

            Assert.Equal([1, 2, 3], bytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AccountPictureServiceIgnoresEmptyPictureFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, []);

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var bytes = await AccountPictureService.ReadFileAsync(file);

            Assert.Null(bytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(24, 60)]
    [InlineData(29, 60)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(120, 120)]
    [InlineData(144, 144)]
    [InlineData(500, 500)]
    [InlineData(501, 501)]
    [InlineData(540, 540)]
    [InlineData(1001, 60)]
    public void RefreshRateFallsBackOnlyForInvalidValues(int refreshRate, int expected)
    {
        Assert.Equal(expected, DisplayRefreshRateService.NormalizeRefreshRate(refreshRate));
    }

    [Theory]
    [InlineData(34, 1.0, 34)]
    [InlineData(34, 1.25, 43)]
    [InlineData(34, 1.5, 51)]
    [InlineData(0, 1.0, 1)]
    [InlineData(34, 0, 34)]
    [InlineData(10, -1.0, 10)]
    public void AppBarHeightUsesPhysicalPixels(double dip, double dpiScale, int expected)
    {
        Assert.Equal(expected, AppBarReservationService.ToPhysicalPixels(dip, dpiScale));
    }

    [Fact]
    public void AppBarTopReservationKeepsNegotiatedTopEdge()
    {
        var negotiated = new NativeRect(0, 40, 1920, 80);

        var reserved = AppBarReservationService.ApplyTopReservationHeight(negotiated, 34);

        Assert.Equal(new NativeRect(0, 40, 1920, 74), reserved);
    }

    [Fact]
    public void ShellMetricsCentersMiniAndExpandedWidths()
    {
        Assert.Equal(838, ShellMetrics.CenterLeft(1920, ShellMetrics.MiniWidth));
        Assert.Equal(470, ShellMetrics.CenterLeft(1920, ShellMetrics.ExpandedWidth));
        Assert.Equal(new ShellGeometry(1920, 32, 34, 0), ShellMetrics.ForMode(isFullBar: true, screenWidth: 1920));
        Assert.Equal(new ShellGeometry(244, 44, 52, 838), ShellMetrics.ForMode(isFullBar: false, screenWidth: 1920));
        Assert.Equal(new ShellGeometry(520, 68, 76, 700), ShellMetrics.MediaToast(1920));
    }

    [Fact]
    public void ShellMetricsKeepAllStatesTopAttachedAndCenteredWhereExpected()
    {
        var mini = ShellMetrics.ForMode(isFullBar: false, screenWidth: 1919);
        var fullBar = ShellMetrics.ForMode(isFullBar: true, screenWidth: 1919);
        var expanded = ShellMetrics.Expanded(1919);

        Assert.Equal(ShellMetrics.MiniWidth, mini.Width);
        Assert.Equal(837.5, mini.Left);
        Assert.Equal(1919, fullBar.Width);
        Assert.Equal(0, fullBar.Left);
        Assert.Equal(469.5, expanded.Left);
        Assert.True(expanded.WindowHeight > expanded.ShellHeight);
    }

    [Fact]
    public void ShellMetricsFitWithinNarrowMonitorWidths()
    {
        Assert.Equal(new ShellGeometry(200, 44, 52, 0), ShellMetrics.ForMode(isFullBar: false, screenWidth: 200));
        Assert.Equal(new ShellGeometry(800, 560, 620, 0), ShellMetrics.Expanded(800));
        Assert.Equal(new ShellGeometry(400, 68, 76, 0), ShellMetrics.MediaToast(400));
        Assert.Equal(new ShellGeometry(0, 32, 34, 0), ShellMetrics.ForMode(isFullBar: true, screenWidth: -1));
    }

    [Theory]
    [InlineData(1920, 1.0, 1920)]
    [InlineData(1920, 1.5, 1280)]
    [InlineData(1920, 0, 1920)]
    public void ShellMetricsConvertsPhysicalScreenWidthToDips(double physicalWidth, double dpiScale, double expected)
    {
        Assert.Equal(expected, ShellMetrics.ToDeviceIndependentWidth(physicalWidth, dpiScale));
    }

    [Fact]
    public void MediaToastIsCompactAndCentered()
    {
        var toast = ShellMetrics.MediaToast(1919);

        Assert.Equal(ShellMetrics.MediaToastWidth, toast.Width);
        Assert.Equal(699.5, toast.Left);
        Assert.True(toast.Width < ShellMetrics.ExpandedWidth);
        Assert.True(toast.WindowHeight < ShellMetrics.ExpandedWindowHeight);
    }

    [Fact]
    public void MiniShellLeavesRoomForHeaderGlyphs()
    {
        Assert.True(ShellMetrics.MiniShellHeight - 10 >= 32);
        Assert.True(ShellMetrics.MiniWidth >= 244);
    }

    [Fact]
    public void HoverCollapseGuardOutlastsShellMotion()
    {
        Assert.True(
            ShellAnimationTiming.CollapseGuardMilliseconds >= ShellAnimationTiming.MotionMilliseconds + 150,
            "Pointer-exit collapse must not interrupt the mini-to-expanded shell animation.");
    }

    [Fact]
    public void DetailRevealStartsDuringShellMotion()
    {
        Assert.InRange(ShellAnimationTiming.DetailRevealDelayMilliseconds, 1, ShellAnimationTiming.MotionMilliseconds - 1);
    }

    [Fact]
    public void MediaToastDurationStaysBrief()
    {
        Assert.InRange(ShellAnimationTiming.MediaToastMilliseconds, 3000, 5000);
    }

    private static MediaSnapshot Media(string title, string artist, MediaState state) =>
        new(title, artist, "Brave", null, state, true, true, true, true);

    private static PriorityStatusSnapshot Status(
        int percent = 80,
        bool charging = false,
        string? wifi = "TELUS1255",
        string? bluetooth = null,
        bool microphone = false,
        bool camera = false) =>
        new(new BatteryInfo(percent, charging), new WifiStatus(wifi, "96%"), bluetooth, microphone, camera);
}
