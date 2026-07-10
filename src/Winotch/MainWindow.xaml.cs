using System.Text;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Winotch.CommandBar;
using Microsoft.Win32;

namespace Winotch;

public partial class MainWindow : FluentWindow
{
    private enum ExpandedPanelMode
    {
        Timer,
        Controls,
        Activity
    }

    private const double FocusLiveProgressWidth = 184;
    private const double ChargingToastFillWidth = 24;
    private const double ChargingToastSweepWidth = 12;
    private static readonly FontFamily ToastTextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily ToastIconFont = new("Segoe MDL2 Assets");
    private static readonly Brush MicLiveBrush = FrozenBrush(Color.FromArgb(255, 255, 159, 10));
    private static readonly Brush MicMutedBrush = FrozenBrush(Color.FromArgb(255, 255, 69, 58));
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _shellTimer = new() { Interval = ShellAnimationTiming.ForegroundPollInterval };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = ShellAnimationTiming.CollapseGuard };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AudioService _audio = new();
    private readonly AudioDeviceService _audioDevices = new();
    private readonly AudioSessionService _audioSessions = new();
    private readonly BrightnessService _brightness = new();
    private readonly WifiService _wifi = new();
    private readonly NotificationService _notifications = new();
    private readonly NotificationChangeTracker _notificationChanges = new();
    private readonly MediaService _media = new();
    private readonly MediaChangeTracker _mediaChanges = new();
    private readonly AccountPictureService _accountPicture = new();
    private readonly AppBarReservationService _appBar = new();
    private readonly PriorityStatusService _priorityStatus = new();
    private readonly PriorityStatusTracker _priorityAlerts = new();
    private readonly SettingsService _settings = new();
    private readonly StartupService _startup = new();
    private readonly CameraMirrorService _cameraMirror = new();
    private readonly TrayIconService _trayIcon;
    private readonly ClipboardHistoryMonitor _clipboardHistory = new();
    private readonly FocusTimerStore _focusTimerStore = new();
    private readonly SystemStatsService _systemStats = new();
    private bool _expanded;
    private bool _compactToastVisible;
    private bool _updatingVolume;
    private bool _notchPaused;
    private bool _exitRequested;
    private bool _updatingControlCenter;
    private ShellMode _currentShellMode = ShellMode.Mini;
    private int _animationFrameRate = 60;
    private MonitorSnapshot? _currentMonitor;
    private DateTime _ignoreHoverUntilUtc;
    private CancellationTokenSource? _expandedReveal;
    private CancellationTokenSource? _compactToastHide;
    private readonly DebouncedBrightnessWriter _brightnessWriter;
    private FrameworkElement? _activeCompactToast;
    private CameraMirrorWindow? _cameraMirrorWindow;
    private IReadOnlyList<NotificationAction> _notificationToastActions = [];
    private int? _lastBatteryPercent;
    private FocusTimerState _focusTimer = FocusTimerState.Stopped;
    private FocusTimerSettings _selectedFocusSettings = FocusTimerSettings.ShortPreset;

    public MainWindow()
    {
        InitializeComponent();
        InitializeLiveActivities();
        InitializeCommandBar();
        _brightnessWriter = new DebouncedBrightnessWriter(TimeSpan.FromMilliseconds(150), _brightness.SetBrightnessAsync);
        _trayIcon = new TrayIconService(this, _settings, _startup, _notifications);
        _settings.Changed += Settings_Changed;
        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            RefreshFocusTimer();
            var now = DateTimeOffset.UtcNow;
            RefreshLiveActivityTimer(now);
            ApplyCalendarUi(now);
            ShowCalendarToastIfDue(now);
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _shellTimer.Tick += (_, _) => ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true);
        _collapseTimer.Tick += (_, _) => CollapseAfterPointerExit();
        _statsTimer.Tick += (_, _) => RefreshSystemStats();
        _calendarTimer.Tick += async (_, _) => await RefreshCalendarAsync();
        _notifications.NotificationsChanged += (_, _) =>
            _ = DispatcherQueue.TryEnqueue(async () => await RefreshStatusAsync());
        _media.MediaChanged += (_, _) =>
            _ = DispatcherQueue.TryEnqueue(async () => await RefreshStatusAsync());
        _clipboardHistory.HistoryChanged += OnClipboardHistoryChanged;
        ClipboardPanel.CopyRequested += ClipboardPanel_CopyRequested;
        ClipboardPanel.DeleteRequested += ClipboardPanel_DeleteRequested;
        ClipboardPanel.ClearRequested += ClipboardPanel_ClearRequested;
        InitializeFeatureFlyouts();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SyncStartupSetting();
        ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: false, force: true);
        LoadFocusTimer();
        ApplyCalendarSettings(_settings.Current);
        ApplyFeatureSettings(_settings.Current);
        UpdateClock();
        RefreshFocusTimer();
        _clockTimer.Start();
        _statusTimer.Start();
        _shellTimer.Start();
        RefreshClipboardPanel();
        RegisterCommandBarHotkey();
        await ApplyAccountPictureAsync();
        await RefreshCalendarAsync();
        await RefreshStatusAsync();
    }

    private async Task ApplyAccountPictureAsync()
    {
        var image = await MediaArtwork.FromBytesAsync(await _accountPicture.ReadAsync());
        if (image is null)
        {
            return;
        }

        LogoAccountPicture.Fill = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
        LogoAccountPicture.Visibility = Visibility.Visible;
        LogoFallbackText.Visibility = Visibility.Collapsed;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var general = _settings.Current.General;
        TimeText.Text = now.ToString(general.Use24HourClock ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        LargeTimeText.Text = now.ToString(general.Use24HourClock ? "HH:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
        LargeDateText.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture);
        LargeDateText.Visibility = general.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        ClipboardPanel.RefreshTimes();
    }

    private void LoadFocusTimer()
    {
        var loaded = _focusTimerStore.Load(DateTimeOffset.UtcNow);
        _focusTimer = loaded.State;
        ShowLatestFocusCompletion(loaded.Completions);
    }

    private void RefreshFocusTimer()
    {
        var now = DateTimeOffset.UtcNow;
        var advanced = _focusTimer.AdvanceTo(now);
        if (advanced.State != _focusTimer)
        {
            _focusTimer = advanced.State;
            _focusTimerStore.Save(_focusTimer);
            ShowLatestFocusCompletion(advanced.Completions);
        }

        ApplyFocusTimerUi(now);
    }

    private void ApplyFocusTimerUi(DateTimeOffset now)
    {
        var snapshot = _focusTimer.SnapshotAt(now);
        var active = snapshot.Status != FocusTimerStatus.Stopped;
        FocusSetupPanel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        FocusRunningPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        FocusLiveActivity.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active)
        {
            FocusLiveProgressFill.Width = 0;
            return;
        }

        FocusPhaseText.Text = snapshot.PhaseLabel;
        FocusRemainingText.Text = snapshot.RemainingText;
        FocusPauseResumeButton.Content = snapshot.Status == FocusTimerStatus.Paused ? "Resume" : "Pause";
        FocusProgressBar.Value = snapshot.Progress * 100;
        FocusLiveRemainingText.Text = snapshot.RemainingText;
        FocusLiveProgressFill.Width = FocusLiveProgressWidth * snapshot.Progress;
        FocusAutoCycleCheckBox.IsChecked = snapshot.AutoCycle;
        RefreshCalendarLiveStatus(now);
    }

    private void ShowLatestFocusCompletion(IReadOnlyList<FocusTimerCompletion> completions)
    {
        if (completions.Count > 0)
        {
            ShowFocusCompletionToast(completions[^1]);
        }
    }

    private void ShowFocusCompletionToast(FocusTimerCompletion completion)
    {
        ResetChargingFlourishToast();
        NotificationToastTitleText.Text = completion.ToastTitle;
        NotificationToastBodyText.Text = "";
        NotificationToastAppText.Text = "Focus Timer";
        NotificationToastTimeText.Text = "Now";
        NotificationToastIconImage.Source = null;
        NotificationToastIconImage.Visibility = Visibility.Collapsed;
        NotificationToastIconFallback.FontFamily = ToastTextFont;
        NotificationToastIconFallback.FontSize = 16;
        NotificationToastIconFallback.Text = "F";
        NotificationToastIconFallback.Visibility = Visibility.Visible;
        ApplyNotificationToastActions([]);
        ShowCompactToast(NotificationToastPanel);
    }

    private async Task RefreshStatusAsync()
    {
        if (_notchPaused)
        {
            return;
        }

        var settings = _settings.Current;
        var battery = ReadBatteryStatus();
        var previousBatteryPercent = _lastBatteryPercent;
        ApplyBatteryStatus(battery);

        ApplyVolumeStatus(ReadVolumeLevel());

        var media = await ReadMediaStatusAsync();
        await ApplyMediaAsync(media);
        if (_mediaChanges.ShouldPop(media) && settings.Toasts.MediaToastsEnabled)
        {
            ShowMediaToast();
        }

        var wifi = await ReadWifiStatusAsync();
        var networks = await ReadWifiNetworksAsync(wifi);
        ApplyWifiStatus(wifi, networks);

        var priorityStatus = ReadPriorityStatus(battery, wifi);
        ApplyLiveActivityInputs(priorityStatus, media);
        ApplyMicState(priorityStatus.MicrophoneActive, ReadCaptureMuted());
        if (_expanded)
        {
            await RefreshControlCenterAsync(priorityStatus);
        }

        var notifications = await ReadNotificationStatusAsync();
        ApplyNotificationStatus(notifications);
        if (_notificationChanges.ShouldPop(notifications.Items) &&
            settings.Toasts.NotificationToastsEnabled &&
            !NotificationSilenceService.IsSilenced())
        {
            ShowNotificationToast(notifications.Items[0]);
        }

        var priorityAlert = _priorityAlerts.Next(priorityStatus, suppressCameraAlert: ShouldSuppressCameraAlert);
        if (priorityAlert is not null && settings.Toasts.PriorityAlertsEnabled)
        {
            ShowPriorityAlertToast(priorityAlert, previousBatteryPercent);
        }
    }

    private BatteryInfo ReadBatteryStatus()
    {
        try
        {
            return SystemStatus.GetBattery();
        }
        catch
        {
            return _lastBatteryPercent is int percent
                ? new BatteryInfo(percent, IsCharging: false)
                : new BatteryInfo(0, IsCharging: false);
        }
    }

    private void ApplyBatteryStatus(BatteryInfo battery)
    {
        var batteryVisual = BatteryVisual.FromPercent(battery.Percent, battery.IsCharging);
        BatteryFill.Width = batteryVisual.FillWidth;
        BatteryFill.Background = batteryVisual.Brush;
        BatteryBar.Foreground = batteryVisual.Brush;
        BatteryText.Text = $"{battery.Percent}%";
        BatteryBar.Value = battery.Percent;
        BatteryDetailText.Text = battery.IsCharging ? "Charging" : $"{battery.Percent}% battery";
        _lastBatteryPercent = battery.Percent;
    }

    private float ReadVolumeLevel()
    {
        try
        {
            return _audio.GetVolume();
        }
        catch
        {
            return 0;
        }
    }

    private void ApplyVolumeStatus(float volume)
    {
        _updatingVolume = true;
        try
        {
            VolumeSlider.Value = volume;
        }
        finally
        {
            _updatingVolume = false;
        }

        VolumeText.Text = $"{volume:0}%";
    }

    private async Task<MediaSnapshot> ReadMediaStatusAsync()
    {
        try
        {
            return await _media.ReadAsync();
        }
        catch
        {
            return MediaSnapshot.Empty;
        }
    }

    private async Task<WifiStatus> ReadWifiStatusAsync()
    {
        try
        {
            return await _wifi.GetCurrentAsync();
        }
        catch
        {
            return new WifiStatus(null, null);
        }
    }

    private async Task<List<WifiNetwork>> ReadWifiNetworksAsync(WifiStatus wifi)
    {
        try
        {
            var networks = (await _wifi.GetNetworksAsync()).ToList();
            if (networks.Count == 0 && wifi.Name is not null)
            {
                networks.Add(new WifiNetwork(wifi.Name, "Connected"));
            }

            return networks;
        }
        catch
        {
            return wifi.Name is null ? [] : [new WifiNetwork(wifi.Name, "Connected")];
        }
    }

    private void ApplyWifiStatus(WifiStatus wifi, IReadOnlyList<WifiNetwork> networks)
    {
        WifiText.Text = wifi.Name is null ? "Offline" : $"{wifi.Name} {wifi.SignalText}";
        var usingConnectedFallback = networks.Count == 1 && networks[0].Signal == "Connected";
        WifiList.ItemsSource = networks;
        WifiList.Visibility = usingConnectedFallback || networks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConnectWifiButton.Visibility = usingConnectedFallback || networks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WifiStateText.Text = wifi.Name is null
            ? "Wi-Fi offline"
            : usingConnectedFallback
                ? "Connected. Location needed to scan Wi-Fi."
                : $"Connected to {wifi.Name}";
    }

    private PriorityStatusSnapshot ReadPriorityStatus(BatteryInfo battery, WifiStatus wifi)
    {
        try
        {
            return _priorityStatus.Read(battery, wifi);
        }
        catch
        {
            return new PriorityStatusSnapshot(
                battery,
                wifi,
                BluetoothDeviceName: null,
                MicrophoneActive: false,
                CameraActive: false);
        }
    }

    private bool ReadCaptureMuted()
    {
        try
        {
            return _audio.GetCaptureMuted();
        }
        catch
        {
            return false;
        }
    }

    private async Task<NotificationSnapshot> ReadNotificationStatusAsync()
    {
        try
        {
            return await _notifications.ReadAsync();
        }
        catch
        {
            return new NotificationSnapshot("Notification status unavailable.", []);
        }
    }

    private void ApplyNotificationStatus(NotificationSnapshot notifications)
    {
        NotificationCountText.Text = notifications.Items.Count.ToString();
        NotificationList.ItemsSource = notifications.Items;
    }

    private void ApplyFeatureSettings(WinotchSettings settings)
    {
        ApplyClipboardHistoryEnabled(settings.Features.ClipboardHistoryEnabled);
        ApplyAppMixerEnabled(settings.Features.ShowAppMixer);
        ApplySystemStatsEnabled(settings.Features.SystemStatsEnabled);
        ApplyLiveActivitySettings(settings.LiveActivities);
        ApplyShelfAndDropletSettings(settings);
    }

    private void ApplyClipboardHistoryEnabled(bool enabled)
    {
        ClipboardPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && IsLoaded)
        {
            _clipboardHistory.Start(this);
            RefreshClipboardPanel();
            return;
        }

        _clipboardHistory.Stop();
    }

    private void ApplyAppMixerEnabled(bool enabled)
    {
        AudioSessionMixerSection.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled)
        {
            return;
        }

        AudioSessionList.ItemsSource = Array.Empty<AudioSessionRow>();
        AudioSessionScroll.Visibility = Visibility.Collapsed;
        NoAudioSessionsText.Visibility = Visibility.Collapsed;
    }

    private void ApplySystemStatsEnabled(bool enabled)
    {
        if (!enabled || !_expanded)
        {
            StopSystemStats();
            return;
        }

        if (!_statsTimer.IsEnabled)
        {
            StartSystemStats();
        }
    }

    public bool IsNotchPaused => _notchPaused;

    public void SetNotchPaused(bool paused)
    {
        if (_notchPaused == paused)
        {
            return;
        }

        _notchPaused = paused;
        if (paused)
        {
            _clockTimer.Stop();
            _statusTimer.Stop();
            _shellTimer.Stop();
            _calendarTimer.Stop();
            _collapseTimer.Stop();
            StopSystemStats();
            _expandedReveal?.Cancel();
            _expandedReveal?.Dispose();
            _expandedReveal = null;
            _expanded = false;
            _ = CloseCameraMirrorAsync();
            _ = CloseShelfAndDropletsAsync();
            HideCompactToast(restoreShell: false);
            _appBar.Release();
            Hide();
            return;
        }

        Show();
        ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: false, force: true);
        UpdateClock();
        RefreshFocusTimer();
        ApplyCalendarSettings(_settings.Current);
        ApplyFeatureSettings(_settings.Current);
        _clockTimer.Start();
        _statusTimer.Start();
        _shellTimer.Start();
        _ = RefreshStatusAsync();
    }

    public void ExitFromTray()
    {
        _exitRequested = true;
        Close();
        (Application.Current as App)?.RequestExit();
    }

    private async Task RefreshControlCenterAsync(PriorityStatusSnapshot? priorityStatus = null)
    {
        _updatingControlCenter = true;
        var showAppMixer = _settings.Current.Features.ShowAppMixer;
        try
        {
            var outputDevices = _audioDevices.GetRenderDevices();
            OutputDeviceList.ItemsSource = outputDevices;
            SelectedOutputDeviceText.Text = outputDevices.FirstOrDefault(device => device.IsDefault)?.Name
                ?? outputDevices.FirstOrDefault()?.Name
                ?? "Output unavailable";

            ApplyAppMixerEnabled(showAppMixer);
            if (showAppMixer)
            {
                var sessions = await _audioSessions.GetSessionsAsync();
                AudioSessionList.ItemsSource = sessions;
                AudioSessionList.Visibility = sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                AudioSessionScroll.Visibility = sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                NoAudioSessionsText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            ApplyMicState(
                priorityStatus?.MicrophoneActive ?? PriorityStatusService.IsMicrophoneActive(),
                _audio.GetCaptureMuted());

            var displays = await _brightness.GetDisplaysAsync();
            BrightnessList.ItemsSource = displays;
            BrightnessBlock.Visibility = displays.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch
        {
            OutputDeviceList.ItemsSource = Array.Empty<AudioOutputDevice>();
            SelectedOutputDeviceText.Text = "Output unavailable";
            AudioSessionList.ItemsSource = Array.Empty<AudioSessionRow>();
            ApplyAppMixerEnabled(showAppMixer);
            if (showAppMixer)
            {
                AudioSessionList.Visibility = Visibility.Collapsed;
                AudioSessionScroll.Visibility = Visibility.Collapsed;
                NoAudioSessionsText.Visibility = Visibility.Visible;
            }

            BrightnessList.ItemsSource = Array.Empty<BrightnessDisplay>();
            BrightnessBlock.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _updatingControlCenter = false;
        }
    }

    private void ApplyMicState(bool isActive, bool isMuted)
    {
        var state = MicPillState.From(isActive, isMuted);
        var foreground = state.Kind switch
        {
            MicPillKind.Live => MicLiveBrush,
            MicPillKind.Muted => MicMutedBrush,
            _ => ResourceBrush("NotchText")
        };

        MicPillGlyph.Text = state.Glyph;
        MicPillText.Text = state.Label;
        MicPillGlyph.Foreground = foreground;
        MicPillText.Foreground = foreground;
        MicPillIndicator.Visibility = state.Kind == MicPillKind.Idle ? Visibility.Collapsed : Visibility.Visible;

        MicRowGlyph.Text = state.Glyph;
        MicRowGlyph.Foreground = foreground;
        MicRowStateText.Text = state.Kind switch
        {
            MicPillKind.Live => "Microphone live",
            MicPillKind.Muted => "Microphone muted",
            _ => "Microphone idle"
        };
        MicMuteButtonText.Text = isMuted ? "Unmute" : "Mute";
        MicMuteButton.Background = isMuted ? MicMutedBrush : ResourceBrush("NotchPanel");
    }

    private async Task ApplyMediaAsync(MediaSnapshot media)
    {
        MediaPanel.Visibility = media.HasMedia ? Visibility.Visible : Visibility.Collapsed;
        NoMediaText.Visibility = media.HasMedia ? Visibility.Collapsed : Visibility.Visible;
        if (!media.HasMedia)
        {
            MediaArtworkImage.Source = null;
            MediaToastArtworkImage.Source = null;
            if (ReferenceEquals(_activeCompactToast, MediaToastPanel))
            {
                HideCompactToast(restoreShell: true);
            }

            return;
        }

        MediaTitleText.Text = media.DisplayTitle;
        MediaArtistText.Text = media.DisplayArtist;
        MediaToastTitleText.Text = media.DisplayTitle;
        MediaToastArtistText.Text = media.DisplayArtist;
        MediaPreviousButton.IsEnabled = media.CanPrevious;
        MediaPlayPauseButton.IsEnabled = media.IsPlaying ? media.CanPause : media.CanPlay;
        MediaNextButton.IsEnabled = media.CanNext;
        MediaPlayPauseIcon.Text = media.IsPlaying ? "\uE769" : "\uE768";
        MediaToastPreviousButton.IsEnabled = media.CanPrevious;
        MediaToastPlayPauseButton.IsEnabled = media.IsPlaying ? media.CanPause : media.CanPlay;
        MediaToastNextButton.IsEnabled = media.CanNext;
        MediaToastPlayPauseIcon.Text = media.IsPlaying ? "\uE769" : "\uE768";

        var artwork = await MediaArtwork.FromBytesAsync(media.Thumbnail);
        MediaArtworkImage.Source = artwork;
        MediaArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        MediaArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
        MediaToastArtworkImage.Source = artwork;
        MediaToastArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        MediaToastArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: false, force: true);
        PositionCameraMirror();
        PositionShelfAndDroplets();
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
        {
            await CloseCameraMirrorAsync();
            await CloseShelfAndDropletsAsync();
        }

        RefreshFocusTimer();
        await RefreshStatusAsync();
    }

    private void Window_MouseEnter(object sender, PointerRoutedEventArgs e)
    {
        if (_commandBarVisible || _compactToastVisible || DateTime.UtcNow < _ignoreHoverUntilUtc)
        {
            return;
        }

        _collapseTimer.Stop();
        SetExpanded(true);
    }

    private void Window_MouseLeave(object sender, PointerRoutedEventArgs e)
    {
        if (_commandBarVisible)
        {
            return;
        }

        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void CollapseAfterPointerExit()
    {
        _collapseTimer.Stop();
        if (!IsMouseOver)
        {
            SetExpanded(false);
        }
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        if (expanded)
        {
            HideCompactToast(restoreShell: false);
        }

        _expanded = expanded;
        _expandedReveal?.Cancel();
        _expandedReveal?.Dispose();
        _expandedReveal = null;
        if (!expanded)
        {
            StopSystemStats();
            // Tool flyouts are independent windows; collapsing the notch back to Mini should not dismiss them.
            ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true, force: true);
            return;
        }

        ShellAnimator.Hide(DateText, _animationFrameRate);
        ShellAnimator.Hide(StatusGroup, _animationFrameRate);
        ShellAnimator.Hide(LiveStrip, _animationFrameRate);
        ClockGroup.HorizontalAlignment = HorizontalAlignment.Center;
        ApplyHeaderDensity(isFullBar: false);
        _appBar.Release();
        SetMouseTransparent(false);
        SelectExpandedPanelMode(ExpandedPanelMode.Controls);
        SetAudioMoreExpanded(false);
        HeaderRow.Height = new GridLength(28);
        NotchShell.Padding = new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = new CornerRadius(0, 0, 34, 34);
        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        DetailPanel.Opacity = 0;
        var monitor = CurrentMonitor(preferCursor: true);
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.PlaceOnMonitor(ShellMetrics.Expanded(monitor.WidthDip), monitor), _animationFrameRate);
        StartSystemStats();
        _ = RefreshControlCenterAsync();
        _expandedReveal = new CancellationTokenSource();
        _ = RevealExpandedContentAsync(_expandedReveal.Token);
    }

    private void StartSystemStats()
    {
        if (!_settings.Current.Features.SystemStatsEnabled)
        {
            StopSystemStats();
            return;
        }

        _statsTimer.Stop();
        _systemStats.BeginSession();
        RefreshSystemStats();
        _statsTimer.Start();
    }

    private void StopSystemStats()
    {
        _statsTimer.Stop();
        _systemStats.EndSession();
        ApplySystemStats(new SystemStatsSnapshot(null, null, null));
    }

    private void RefreshSystemStats()
    {
        if (!_expanded || !_settings.Current.Features.SystemStatsEnabled)
        {
            StopSystemStats();
            return;
        }

        ApplySystemStats(_systemStats.Read());
    }

    private void ApplySystemStats(SystemStatsSnapshot snapshot)
    {
        // Keep Agenda and Clipboard available even when the optional stats sampler has no rows.
        StatsRowsPanel.Visibility = snapshot.HasRows ? Visibility.Visible : Visibility.Collapsed;
        ApplyStatsRow(StatsCpuRow, StatsCpuValueText, snapshot.Cpu);
        ApplyStatsRow(StatsRamRow, StatsRamValueText, snapshot.Ram);
        ApplyStatsRow(StatsNetRow, StatsNetValueText, snapshot.Network);
    }

    private static void ApplyStatsRow(
        UIElement row,
        TextBlock valueText,
        SystemStatRowSnapshot? snapshot)
    {
        row.Visibility = snapshot is null ? Visibility.Collapsed : Visibility.Visible;
        if (snapshot is null)
        {
            valueText.Text = "";
        ToolTipService.SetToolTip(valueText, null);
            return;
        }

        valueText.Text = snapshot.ValueText;
        ToolTipService.SetToolTip(valueText, snapshot.ValueText);
    }

    private void ShowMediaToast()
    {
        ResetChargingFlourishToast();
        ShowCompactToast(MediaToastPanel);
    }

    private async void ShowNotificationToast(NotificationItem notification)
    {
        ResetChargingFlourishToast();
        NotificationToastTitleText.Text = notification.Title;
        NotificationToastBodyText.Text = notification.Body;
        NotificationToastAppText.Text = notification.App;
        NotificationToastTimeText.Text = notification.TimeText;
        NotificationToastIconFallback.FontFamily = ToastTextFont;
        NotificationToastIconFallback.FontSize = 16;
        NotificationToastIconFallback.Text = notification.BadgeText;

        var icon = await MediaArtwork.FromBytesAsync(notification.Icon);
        NotificationToastIconImage.Source = icon;
        NotificationToastIconImage.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        NotificationToastIconFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;

        ApplyNotificationToastActions(notification.Actions);

        ShowCompactToast(NotificationToastPanel);
    }

    private void ShowPriorityAlertToast(PriorityStatusAlert alert, int? previousBatteryPercent)
    {
        ResetChargingFlourishToast();
        NotificationToastTitleText.Text = alert.Title;
        NotificationToastBodyText.Text = alert.Body;
        NotificationToastAppText.Text = "System Status";
        NotificationToastTimeText.Text = "Now";
        NotificationToastIconImage.Source = null;
        NotificationToastIconImage.Visibility = Visibility.Collapsed;
        NotificationToastIconFallback.FontFamily = ToastIconFont;
        NotificationToastIconFallback.FontSize = 17;
        NotificationToastIconFallback.Text = alert.Icon;
        NotificationToastIconFallback.Visibility = Visibility.Visible;
        ApplyNotificationToastActions([]);
        var flourishPercent = alert.ShowsChargingFlourish ? alert.BatteryPercent : null;
        if (flourishPercent is int batteryPercent)
        {
            ApplyChargingFlourishToast(batteryPercent);
        }

        ShowCompactToast(NotificationToastPanel);
        if (flourishPercent is int percent)
        {
            StartChargingFlourish(percent, previousBatteryPercent);
        }
    }

    private void ApplyChargingFlourishToast(int percent)
    {
        var clampedPercent = Math.Clamp(percent, 0, 100);
        var chargingVisual = BatteryVisual.FromPercent(clampedPercent, isCharging: true);
        NotificationToastIconImage.Visibility = Visibility.Collapsed;
        NotificationToastIconFallback.Visibility = Visibility.Collapsed;
        ChargingToastBatteryGlyph.Visibility = Visibility.Visible;
        ChargingToastBatteryShell.BorderBrush = chargingVisual.Brush;
        ChargingToastBatteryFillBrush.Background = chargingVisual.Brush;
        ChargingToastBatteryTerminal.Background = chargingVisual.Brush;
        ChargingToastPercentText.Text = $"{clampedPercent}%";
        ChargingToastPercentText.Foreground = chargingVisual.Brush;
        ChargingToastPercentText.Visibility = Visibility.Visible;
    }

    private void StartChargingFlourish(int percent, int? previousBatteryPercent)
    {
        var animation = BatteryVisual.ChargingFillAnimation(percent, previousBatteryPercent, ChargingToastFillWidth);
        ChargingToastBatteryFill.Width = animation.FromWidth;
        ChargingToastSweepTransform.X = -ChargingToastSweepWidth;
        ChargingToastTintSweep.Opacity = 0.62;

        ShellAnimator.Animate(
            ChargingToastBatteryFill,
            FrameworkElement.WidthProperty,
            animation.ToWidth,
            _animationFrameRate);
        ChargingToastSweepTransform.X = animation.ToWidth + ChargingToastSweepWidth;
        ShellAnimator.Animate(
            ChargingToastTintSweep,
            UIElement.OpacityProperty,
            0,
            _animationFrameRate);
    }

    private void ResetChargingFlourishToast()
    {
        ChargingToastBatteryFill.Width = 0;
        ChargingToastSweepTransform.X = -ChargingToastSweepWidth;
        ChargingToastTintSweep.Opacity = 0;
        ChargingToastBatteryGlyph.Visibility = Visibility.Collapsed;
        ChargingToastPercentText.Visibility = Visibility.Collapsed;
    }

    private void ApplyNotificationToastActions(IReadOnlyList<NotificationAction> actions)
    {
        _notificationToastActions = actions.Take(2).ToArray();
        NotificationToastActionsPanel.Visibility = _notificationToastActions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NotificationToastPrimaryActionButton.Visibility = _notificationToastActions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationToastSecondaryActionButton.Visibility = _notificationToastActions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        NotificationToastPrimaryActionText.Text = _notificationToastActions.Count > 0 ? _notificationToastActions[0].Label : "";
        NotificationToastSecondaryActionText.Text = _notificationToastActions.Count > 1 ? _notificationToastActions[1].Label : "";
    }

    private void ShowCompactToast(FrameworkElement panel)
    {
        if (_expanded || _commandBarVisible)
        {
            return;
        }

        HideCompactToast(restoreShell: false);
        var hide = new CancellationTokenSource();
        _compactToastHide = hide;
        _compactToastVisible = true;
        _activeCompactToast = panel;

        _appBar.Release();
        SetMouseTransparent(false);
        ShellAnimator.Hide(ClockGroup, _animationFrameRate);
        ShellAnimator.Hide(StatusGroup, _animationFrameRate);
        ShellAnimator.Hide(LiveStrip, _animationFrameRate);
        DetailPanel.Opacity = 0;
        HeaderRow.Height = new GridLength(28);
        NotchShell.Padding = new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = new CornerRadius(0, 0, 24, 24);
        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        var monitor = CurrentMonitor();
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.PlaceOnMonitor(ShellMetrics.MediaToast(monitor.WidthDip), monitor), _animationFrameRate);
        panel.Opacity = 0;
        ShellAnimator.Show(panel, _animationFrameRate);
        _ = HideCompactToastAfterDelayAsync(hide);
    }

    private async Task HideCompactToastAfterDelayAsync(CancellationTokenSource hide)
    {
        try
        {
            await Task.Delay(_settings.Current.Toasts.DurationScale.ApplyTo(ShellAnimationTiming.MediaToastDuration), hide.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        HideCompactToast(restoreShell: true, owner: hide);
    }

    private void HideCompactToast(bool restoreShell, CancellationTokenSource? owner = null)
    {
        if (owner is not null && !ReferenceEquals(_compactToastHide, owner))
        {
            owner.Dispose();
            return;
        }

        _compactToastHide?.Cancel();
        _compactToastHide?.Dispose();
        _compactToastHide = null;
        if (!_compactToastVisible && _activeCompactToast is null)
        {
            return;
        }

        _compactToastVisible = false;
        _ignoreHoverUntilUtc = DateTime.UtcNow + ShellAnimationTiming.CollapseGuard;
        if (_activeCompactToast is not null)
        {
            ShellAnimator.Hide(_activeCompactToast, _animationFrameRate);
            _activeCompactToast = null;
        }

        ClockGroup.Visibility = Visibility.Visible;
        ClockGroup.Opacity = 1;
        if (restoreShell)
        {
            ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true, force: true);
        }
    }

    private async Task RevealExpandedContentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ShellAnimationTiming.DetailRevealDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_expanded || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ShellAnimator.Animate(DetailPanel, UIElement.OpacityProperty, 1, _animationFrameRate);
        try
        {
            await Task.Delay(ShellAnimationTiming.DetailRevealCompletionDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_expanded || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        SetMouseTransparent(false);
        ClockGroup.HorizontalAlignment = HorizontalAlignment.Left;
        HeaderRow.Height = new GridLength(48);
        NotchShell.Padding = new Thickness(18, 8, 18, 12);
        if (_settings.Current.General.ShowDate)
        {
            ShellAnimator.Show(DateText, _animationFrameRate);
        }
        else
        {
            ShellAnimator.Hide(DateText, _animationFrameRate);
        }

        ShellAnimator.Show(StatusGroup, _animationFrameRate);
    }

    private void ApplyShellMode(ShellMode mode, bool animate = true)
    {
        if (_expanded || _compactToastVisible || (_commandBarVisible && mode != ShellMode.Command))
        {
            return;
        }

        _currentShellMode = mode;
        var isFullBar = mode == ShellMode.FullBar;
        var isLive = mode == ShellMode.Live;
        var monitor = CurrentMonitor();
        var geometry = ShellMetrics.PlaceOnMonitor(
            isLive ? ShellMetrics.LiveStrip(monitor.WidthDip) : ShellMetrics.ForMode(isFullBar, monitor.WidthDip),
            monitor);

        ShellAnimator.Hide(DateText, _animationFrameRate);
        ClockGroup.Visibility = isLive ? Visibility.Collapsed : Visibility.Visible;
        ClockGroup.Opacity = isLive ? 0 : 1;
        StatusGroup.Visibility = isFullBar ? Visibility.Visible : Visibility.Collapsed;
        StatusGroup.Opacity = isFullBar ? 1 : 0;
        LiveStrip.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
        LiveStrip.Opacity = isLive ? 1 : 0;
        ApplyHeaderDensity(isFullBar);
        ClockGroup.HorizontalAlignment = isFullBar ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        HeaderRow.Height = new GridLength(isLive ? 44 : 28);
        NotchShell.Padding = isFullBar
            ? new Thickness(10, 2, 10, 2)
            : isLive
                ? new Thickness(12, 7, 12, 7)
                : new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = isFullBar ? new CornerRadius(0) : new CornerRadius(0, 0, isLive ? 28 : 20, isLive ? 28 : 20);
        if (isFullBar)
        {
            _appBar.ReserveTop(this, geometry.WindowHeight, monitor);
        }
        else
        {
            _appBar.Release();
        }

        if (animate)
        {
            ShellAnimator.Animate(DetailPanel, UIElement.OpacityProperty, 0, _animationFrameRate);
            ShellAnimator.AnimateShell(this, NotchShell, geometry, _animationFrameRate);
            if (isLive)
            {
                ShellAnimator.Show(LiveStrip, _animationFrameRate);
            }
            else
            {
                ShellAnimator.Hide(LiveStrip, _animationFrameRate);
            }

            SetMouseTransparent(isFullBar && !_expanded);
            return;
        }

        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        DetailPanel.Opacity = 0;
        var hostGeometry = isFullBar
            ? geometry
            : ShellMetrics.PlaceOnMonitor(ShellMetrics.Expanded(monitor.WidthDip), monitor);
        ShellAnimator.SetShellGeometry(this, NotchShell, geometry, hostGeometry);
        SetMouseTransparent(isFullBar && !_expanded);
    }

    private void ApplyHeaderDensity(bool isFullBar)
    {
        LogoBadge.Width = isFullBar ? 24 : 28;
        LogoBadge.Height = isFullBar ? 24 : 28;
        LogoBadge.CornerRadius = new CornerRadius(isFullBar ? 12 : 14);
        TimeText.FontSize = isFullBar ? 15 : 17;

        foreach (var chip in StatusGroup.Children.OfType<Border>())
        {
            chip.Padding = isFullBar ? new Thickness(9, 4, 9, 4) : new Thickness(11, 7, 11, 7);
            chip.CornerRadius = new CornerRadius(isFullBar ? 13 : 17);
        }
    }

    private void ApplyForegroundState(ForegroundWindowSnapshot foreground, bool animate, bool force = false)
    {
        if (_commandBarVisible)
        {
            return;
        }

        var monitors = MonitorTargeting.CaptureScreens();
        if (monitors.Count == 0)
        {
            return;
        }

        var followActiveMonitor = _settings.Current.Features.FollowActiveMonitor;
        var targetMonitor = MonitorTargeting.SelectMonitor(
            monitors,
            followActiveMonitor
                ? new MonitorTargetRequest(
                    foreground.WindowRect,
                    foreground.UseCursorMonitor,
                    MonitorTargeting.GetCursorPosition(),
                    _currentMonitor?.DeviceName)
                : new MonitorTargetRequest(
                    ForegroundRect: null,
                    UseCursorMonitor: false,
                    CursorPosition: System.Drawing.Point.Empty,
                    LastMonitorDeviceName: null));
        var monitorChanged = _currentMonitor is null ||
            !string.Equals(_currentMonitor.Value.DeviceName, targetMonitor.DeviceName, StringComparison.OrdinalIgnoreCase);
        var desiredMode = DesiredShellModeForLiveActivity(foreground.Mode);
        if (!force && !monitorChanged && desiredMode == _currentShellMode)
        {
            return;
        }

        if (monitorChanged)
        {
            _appBar.Release();
        }

        _currentMonitor = targetMonitor;
        _animationFrameRate = DisplayRefreshRateService.GetRefreshRate(targetMonitor.DeviceName);
        ApplyShellMode(desiredMode, animate);
    }

    private MonitorSnapshot CurrentMonitor(bool preferCursor = false)
    {
        if (!preferCursor && _currentMonitor is MonitorSnapshot monitor)
        {
            return monitor;
        }

        var monitors = MonitorTargeting.CaptureScreens();
        var selected = MonitorTargeting.SelectMonitor(
            monitors,
            new MonitorTargetRequest(
                ForegroundRect: null,
                UseCursorMonitor: preferCursor,
                MonitorTargeting.GetCursorPosition(),
                _currentMonitor?.DeviceName));
        _currentMonitor = selected;
        return selected;
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingVolume || !IsLoaded)
        {
            return;
        }

        _audio.SetVolume((float)e.NewValue);
        VolumeText.Text = $"{e.NewValue:0}%";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _trayIcon.OpenSettings();
    }

    private void ViewNotifications_Click(object sender, RoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Activity);
        NotificationList.Focus(FocusState.Programmatic);
    }

    private void TimerModeTab_Click(object sender, RoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Timer);
    }

    private void TimerModeTab_PreviewMouseLeftButtonDown(object sender, PointerRoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Timer);
    }

    private void ControlsModeTab_Click(object sender, RoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Controls);
    }

    private void ControlsModeTab_PreviewMouseLeftButtonDown(object sender, PointerRoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Controls);
    }

    private void ActivityModeTab_Click(object sender, RoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Activity);
        NotificationList.Focus(FocusState.Programmatic);
    }

    private void ActivityModeTab_PreviewMouseLeftButtonDown(object sender, PointerRoutedEventArgs e)
    {
        SelectExpandedPanelMode(ExpandedPanelMode.Activity);
        NotificationList.Focus(FocusState.Programmatic);
    }

    private void AudioMoreToggle_Click(object sender, RoutedEventArgs e)
    {
        SetAudioMoreExpanded(AudioMorePanel.Visibility != Visibility.Visible);
    }

    private void SetAudioMoreExpanded(bool expanded)
    {
        AudioMorePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AudioMoreChevron.Text = expanded ? "\uE70E" : "\uE70D";
    }

    private void SelectExpandedPanelMode(ExpandedPanelMode mode)
    {
        var activityActive = mode == ExpandedPanelMode.Activity;
        var timerActive = mode == ExpandedPanelMode.Timer;

        ControlsTabContent.Visibility = activityActive ? Visibility.Collapsed : Visibility.Visible;
        ActivitySection.Visibility = activityActive ? Visibility.Visible : Visibility.Collapsed;
        AudioControlsSection.Visibility = timerActive ? Visibility.Collapsed : Visibility.Visible;
        NowPlayingSection.Visibility = timerActive ? Visibility.Collapsed : Visibility.Visible;
        NowSection.Margin = timerActive ? new Thickness(0) : new Thickness(0, 0, 0, 12);
        Grid.SetColumn(TimerColumn, timerActive ? 0 : 1);
        Grid.SetColumnSpan(TimerColumn, timerActive ? 2 : 1);

        ApplyExpandedTabState(ControlsModeTab, ControlsModeIcon, ControlsModeText, mode == ExpandedPanelMode.Controls);
        ApplyExpandedTabState(ActivityModeTab, ActivityModeIcon, ActivityModeText, activityActive);
        ApplyExpandedTabState(NowModeTab, NowModeIcon, NowModeText, timerActive);
    }

    private void ApplyExpandedTabState(Button tab, TextBlock icon, TextBlock text, bool active)
    {
        tab.Background = active ? ResourceBrush("NotchPanel") : ResourceBrush("NotchHitTestFill");
        tab.BorderBrush = active ? ResourceBrush("NotchStroke") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        icon.Foreground = active ? ResourceBrush("NotchText") : ResourceBrush("NotchMutedText");
        text.Foreground = active ? ResourceBrush("NotchText") : ResourceBrush("NotchMutedText");
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage
        {
            RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
        };
        package.SetText(await DiagnosticsReport.CaptureAsync(_settings.Current, _startup));
        package.Properties.Add("ExcludeClipboardContentFromMonitorProcessing", true);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    private void FocusShortPreset_Click(object sender, RoutedEventArgs e)
    {
        _selectedFocusSettings = FocusTimerSettings.ShortPreset;
        FocusCustomMinutesBox.Text = "";
        FocusValidationText.Visibility = Visibility.Collapsed;
    }

    private void FocusLongPreset_Click(object sender, RoutedEventArgs e)
    {
        _selectedFocusSettings = FocusTimerSettings.LongPreset;
        FocusCustomMinutesBox.Text = "";
        FocusValidationText.Visibility = Visibility.Collapsed;
    }

    private void FocusStart_Click(object sender, RoutedEventArgs e)
    {
        var autoCycle = FocusAutoCycleCheckBox.IsChecked == true;
        FocusTimerSettings settings;
        if (string.IsNullOrWhiteSpace(FocusCustomMinutesBox.Text))
        {
            settings = _selectedFocusSettings with { AutoCycle = autoCycle };
        }
        else if (!FocusTimerSettings.TryCreateCustom(FocusCustomMinutesBox.Text, autoCycle, out settings, out var error))
        {
            FocusValidationText.Text = error;
            FocusValidationText.Visibility = Visibility.Visible;
            return;
        }

        _focusTimer = FocusTimerState.Start(settings, DateTimeOffset.UtcNow);
        FocusValidationText.Visibility = Visibility.Collapsed;
        _focusTimerStore.Save(_focusTimer);
        ApplyFocusTimerUi(DateTimeOffset.UtcNow);
    }

    private void FocusPauseResume_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        _focusTimer = _focusTimer.Status == FocusTimerStatus.Paused
            ? _focusTimer.Resume(now)
            : _focusTimer.Pause(now);
        _focusTimerStore.Save(_focusTimer);
        ApplyFocusTimerUi(now);
    }

    private void FocusSkip_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        _focusTimer = _focusTimer.Skip(now);
        _focusTimerStore.Save(_focusTimer);
        ApplyFocusTimerUi(now);
    }

    private void FocusStop_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer = FocusTimerState.Stopped;
        _focusTimerStore.Clear();
        ApplyFocusTimerUi(DateTimeOffset.UtcNow);
    }

    private async void OutputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AudioOutputDevice device } || device.IsDefault)
        {
            return;
        }

        _audioDevices.SetDefaultRenderDevice(device.Id);
        _audio.RefreshDefaultEndpoints();
        await RefreshStatusAsync();
    }

    private void AudioSessionVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControlCenter || !IsLoaded ||
            sender is not Slider { DataContext: AudioSessionRow session })
        {
            return;
        }

        _audioSessions.SetSessionVolume(session.Id, (float)e.NewValue);
    }

    private async void AudioSessionMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AudioSessionRow session })
        {
            return;
        }

        _audioSessions.SetSessionMuted(session.Id, !session.IsMuted);
        await RefreshControlCenterAsync();
    }

    private async void MicMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.SetCaptureMuted(!_audio.GetCaptureMuted());
        await RefreshControlCenterAsync();
    }

    private async void CameraMirror_Click(object sender, RoutedEventArgs e)
    {
        if (_cameraMirrorWindow is not null)
        {
            await CloseCameraMirrorAsync();
            return;
        }

        _cameraMirrorWindow = new CameraMirrorWindow(_cameraMirror)
        {
            Owner = this
        };
        _cameraMirrorWindow.Closed += CameraMirrorWindow_Closed;
        PositionCameraMirror();
        _cameraMirrorWindow.Show();
        _cameraMirrorWindow.Activate();
        await _cameraMirror.OpenAsync();
    }

    private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControlCenter || !IsLoaded ||
            sender is not Slider { DataContext: BrightnessDisplay display })
        {
            return;
        }

        _brightnessWriter.Queue(display, (int)Math.Round(e.NewValue));
    }

    private async void ConnectWifi_Click(object sender, RoutedEventArgs e)
    {
        if (WifiList.SelectedItem is not WifiNetwork network)
        {
            WifiStateText.Text = "Select a saved Wi-Fi profile.";
            return;
        }

        WifiStateText.Text = await _wifi.ConnectAsync(network.Name);
    }

    private async void MediaPrevious_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaActionAsync(_media.PreviousAsync);
    }

    private async void MediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaActionAsync(_media.TogglePlayPauseAsync);
    }

    private async void MediaNext_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaActionAsync(_media.NextAsync);
    }

    private async void NotificationToastPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        await RunNotificationActionAsync(0);
    }

    private async void NotificationToastSecondaryAction_Click(object sender, RoutedEventArgs e)
    {
        await RunNotificationActionAsync(1);
    }

    private void OnClipboardHistoryChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(RefreshClipboardPanel);
    }

    private void RefreshClipboardPanel()
    {
        ClipboardPanel.SetItems(_clipboardHistory.Items);
    }

    private async void ClipboardPanel_CopyRequested(object? sender, ClipboardHistoryEntry entry)
    {
        if (await _clipboardHistory.CopyToClipboardAsync(entry.Id))
        {
            ClipboardPanel.ShowCopied(entry.Id);
        }
    }

    private void ClipboardPanel_DeleteRequested(object? sender, Guid id)
    {
        _clipboardHistory.Delete(id);
    }

    private void ClipboardPanel_ClearRequested(object? sender, EventArgs e)
    {
        _clipboardHistory.Clear();
    }

    private async Task RunMediaActionAsync(Func<Task> action)
    {
        await action();
        await RefreshStatusAsync();
    }

    private async Task RunNotificationActionAsync(int index)
    {
        if (index < 0 || index >= _notificationToastActions.Count)
        {
            return;
        }

        await _notificationToastActions[index].InvokeAsync();
        HideCompactToast(restoreShell: true);
        await RefreshStatusAsync();
    }

    private void Settings_Changed(object? sender, WinotchSettings settings)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateClock();
            ApplyCalendarSettings(settings);
            ApplyFeatureSettings(settings);
            ApplyCommandBarSettings(settings);
            if (!_expanded || !settings.General.ShowDate)
            {
                ShellAnimator.Hide(DateText);
            }
            else
            {
                ShellAnimator.Show(DateText, _animationFrameRate);
            }

            ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true, force: true);
        });
    }

    private void SyncStartupSetting()
    {
        var state = _startup.GetState(StartupService.CurrentExecutablePath());
        if (!state.CanAccess)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            General = settings.General with { StartWithWindows = state.IsEnabled }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            SetNotchPaused(true);
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _expandedReveal?.Cancel();
        _expandedReveal?.Dispose();
        _compactToastHide?.Cancel();
        _compactToastHide?.Dispose();
        _statsTimer.Stop();
        _systemStats.Dispose();
        _trayIcon.Dispose();
        _settings.Changed -= Settings_Changed;
        _brightnessWriter.Dispose();
        _audio.Dispose();
        _cameraMirror.Dispose();
        _appBar.Dispose();
        _notifications.Dispose();
        _clipboardHistory.Dispose();
        _calendarTimer.Stop();
        _calendarRefresh.Dispose();
        DisposeCommandBar();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        base.OnClosed(e);
    }

    private void SetMouseTransparent(bool enabled)
    {
        WindowChromeInterop.SetMouseTransparent(this, enabled && !_expanded && DetailPanel.Opacity <= 0);
    }

    private static Brush FrozenBrush(Color color)
    {
        return new SolidColorBrush(color);
    }

    private static Brush ResourceBrush(string key) =>
        (Brush)Application.Current.Resources[key];

    private bool ShouldSuppressCameraAlert =>
        CameraMirrorLifecycle.SuppressesCameraAlerts(_cameraMirror.State);

    private async Task CloseCameraMirrorAsync()
    {
        var window = _cameraMirrorWindow;
        if (window is null)
        {
            await _cameraMirror.CloseAsync();
            return;
        }

        await window.CloseMirrorAsync();
    }

    private void CameraMirrorWindow_Closed(object sender, WindowEventArgs e)
    {
        if (sender is CameraMirrorWindow window)
        {
            window.Closed -= CameraMirrorWindow_Closed;
        }

        if (ReferenceEquals(_cameraMirrorWindow, sender))
        {
            _cameraMirrorWindow = null;
        }
    }

    private void PositionCameraMirror()
    {
        if (_cameraMirrorWindow is null)
        {
            return;
        }

        var left = Left + (Width - _cameraMirrorWindow.Width) / 2;
        var shellHeight = NotchShell.ActualHeight > 0 ? NotchShell.ActualHeight : NotchShell.Height;
        var top = Top + shellHeight + 8;
        var monitor = CurrentMonitor();
        var minLeft = monitor.WorkAreaLeftDip + 8;
        var maxLeft = monitor.WorkAreaRightDip - _cameraMirrorWindow.Width - 8;
        var minTop = monitor.WorkAreaTopDip + 8;
        var maxTop = monitor.WorkAreaBottomDip - _cameraMirrorWindow.Height - 8;
        _cameraMirrorWindow.Left = ClampToRange(left, minLeft, maxLeft);
        _cameraMirrorWindow.Top = ClampToRange(top, minTop, maxTop);
    }

    private static double ClampToRange(double value, double minimum, double maximum) =>
        maximum < minimum ? minimum : Math.Min(Math.Max(minimum, value), maximum);
}
