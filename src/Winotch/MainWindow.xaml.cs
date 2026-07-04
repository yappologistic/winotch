using System.Text;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Winotch;

public partial class MainWindow : Window
{
    private const double FocusLiveProgressWidth = 26;
    private static readonly System.Windows.Media.FontFamily ToastTextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly System.Windows.Media.FontFamily ToastIconFont = new("Segoe MDL2 Assets");
    private static readonly System.Windows.Media.Brush MicLiveBrush = FrozenBrush(System.Windows.Media.Color.FromRgb(255, 159, 10));
    private static readonly System.Windows.Media.Brush MicMutedBrush = FrozenBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = ShellAnimationTiming.CollapseGuard };
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
    private readonly TrayIconService _trayIcon;
    private readonly ClipboardHistoryMonitor _clipboardHistory = new();
    private readonly FocusTimerStore _focusTimerStore = new();
    private bool _expanded;
    private bool _compactToastVisible;
    private bool _updatingVolume;
    private bool _notchPaused;
    private bool _exitRequested;
    private bool _updatingControlCenter;
    private int _animationFrameRate = 60;
    private DateTime _ignoreHoverUntilUtc;
    private CancellationTokenSource? _expandedReveal;
    private CancellationTokenSource? _compactToastHide;
    private readonly DebouncedBrightnessWriter _brightnessWriter;
    private FrameworkElement? _activeCompactToast;
    private IReadOnlyList<NotificationAction> _notificationToastActions = [];
    private FocusTimerState _focusTimer = FocusTimerState.Stopped;
    private FocusTimerSettings _selectedFocusSettings = FocusTimerSettings.ShortPreset;

    public MainWindow()
    {
        InitializeComponent();
        InitializeFileShelf();
        _brightnessWriter = new DebouncedBrightnessWriter(TimeSpan.FromMilliseconds(150), _brightness.SetBrightnessAsync);
        _trayIcon = new TrayIconService(this, _settings, _startup);
        _settings.Changed += Settings_Changed;
        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            RefreshFocusTimer();
            var now = DateTimeOffset.UtcNow;
            ApplyCalendarUi(now);
            ShowCalendarToastIfDue(now);
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _shellTimer.Tick += (_, _) => ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
        _collapseTimer.Tick += (_, _) => CollapseAfterPointerExit();
        _calendarTimer.Tick += async (_, _) => await RefreshCalendarAsync();
        _notifications.NotificationsChanged += (_, _) => Dispatcher.Invoke(async () => await RefreshStatusAsync());
        _media.MediaChanged += (_, _) => Dispatcher.Invoke(async () => await RefreshStatusAsync());
        _clipboardHistory.HistoryChanged += OnClipboardHistoryChanged;
        ClipboardPanel.CopyRequested += ClipboardPanel_CopyRequested;
        ClipboardPanel.DeleteRequested += ClipboardPanel_DeleteRequested;
        ClipboardPanel.ClearRequested += ClipboardPanel_ClearRequested;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _animationFrameRate = DisplayRefreshRateService.GetPrimaryRefreshRate();
        SyncStartupSetting();
        ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
        LoadFocusTimer();
        ApplyCalendarSettings(_settings.Current);
        UpdateClock();
        RefreshFocusTimer();
        _clockTimer.Start();
        _statusTimer.Start();
        _shellTimer.Start();
        _clipboardHistory.Start(this);
        RefreshClipboardPanel();
        await ApplyAccountPictureAsync();
        await LoadFileShelfAsync();
        await RefreshCalendarAsync();
        await RefreshStatusAsync();
    }

    private async Task ApplyAccountPictureAsync()
    {
        var image = MediaArtwork.FromBytes(await _accountPicture.ReadAsync());
        if (image is null)
        {
            return;
        }

        LogoAccountPicture.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
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
        var battery = SystemStatus.GetBattery();
        var batteryVisual = BatteryVisual.FromPercent(battery.Percent, battery.IsCharging);
        BatteryFill.Width = batteryVisual.FillWidth;
        BatteryFill.Background = batteryVisual.Brush;
        BatteryBar.Foreground = batteryVisual.Brush;
        BatteryText.Text = $"{battery.Percent}%";
        BatteryBar.Value = battery.Percent;
        BatteryDetailText.Text = battery.IsCharging ? "Charging" : $"{battery.Percent}% battery";

        var volume = _audio.GetVolume();
        _updatingVolume = true;
        VolumeSlider.Value = volume;
        _updatingVolume = false;
        VolumeText.Text = $"{volume:0}%";

        var media = await _media.ReadAsync();
        ApplyMedia(media);
        if (_mediaChanges.ShouldPop(media) && settings.Toasts.MediaToastsEnabled)
        {
            ShowMediaToast();
        }

        var wifi = await _wifi.GetCurrentAsync();
        WifiText.Text = wifi.Name is null ? "Offline" : $"{wifi.Name} {wifi.SignalText}";
        var networks = (await _wifi.GetNetworksAsync()).ToList();
        if (networks.Count == 0 && wifi.Name is not null)
        {
            networks.Add(new WifiNetwork(wifi.Name, "Connected"));
        }

        var usingConnectedFallback = networks.Count == 1 && networks[0].Signal == "Connected";
        WifiList.ItemsSource = networks;
        WifiList.Visibility = usingConnectedFallback || networks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConnectWifiButton.Visibility = usingConnectedFallback || networks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WifiStateText.Text = wifi.Name is null
            ? "No connected Wi-Fi"
            : usingConnectedFallback
                ? $"{wifi.Name} connected. Scan needs Windows Location permission."
                : $"Connected to {wifi.Name}";
        var priorityStatus = _priorityStatus.Read(battery, wifi);
        ApplyMicState(priorityStatus.MicrophoneActive, _audio.GetCaptureMuted());
        if (_expanded)
        {
            await RefreshControlCenterAsync(priorityStatus);
        }

        var notifications = await _notifications.ReadAsync();
        NotificationStateText.Text = notifications.Status;
        NotificationCountText.Text = notifications.Items.Count.ToString();
        NotificationList.ItemsSource = notifications.Items;
        if (_notificationChanges.ShouldPop(notifications.Items) &&
            settings.Toasts.NotificationToastsEnabled &&
            !NotificationSilenceService.IsSilenced())
        {
            ShowNotificationToast(notifications.Items[0]);
        }

        var priorityAlert = _priorityAlerts.Next(priorityStatus);
        if (priorityAlert is not null && settings.Toasts.PriorityAlertsEnabled)
        {
            ShowPriorityAlertToast(priorityAlert);
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
            _expandedReveal?.Cancel();
            _expandedReveal?.Dispose();
            _expandedReveal = null;
            _expanded = false;
            HideCompactToast(restoreShell: false);
            _appBar.Release();
            Hide();
            return;
        }

        Show();
        _animationFrameRate = DisplayRefreshRateService.GetPrimaryRefreshRate();
        ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
        UpdateClock();
        RefreshFocusTimer();
        ApplyCalendarSettings(_settings.Current);
        _clockTimer.Start();
        _statusTimer.Start();
        _shellTimer.Start();
        _ = RefreshStatusAsync();
    }

    public void ExitFromTray()
    {
        _exitRequested = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private async Task RefreshControlCenterAsync(PriorityStatusSnapshot? priorityStatus = null)
    {
        _updatingControlCenter = true;
        try
        {
            OutputDeviceList.ItemsSource = _audioDevices.GetRenderDevices();

            var sessions = _audioSessions.GetSessions();
            AudioSessionList.ItemsSource = sessions;
            AudioSessionList.Visibility = sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            AudioSessionScroll.Visibility = sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoAudioSessionsText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
            AudioSessionList.ItemsSource = Array.Empty<AudioSessionRow>();
            AudioSessionScroll.Visibility = Visibility.Collapsed;
            BrightnessList.ItemsSource = Array.Empty<BrightnessDisplay>();
            BrightnessBlock.Visibility = Visibility.Collapsed;
            NoAudioSessionsText.Visibility = Visibility.Visible;
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
            _ => (System.Windows.Media.Brush)FindResource("NotchText")
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
        MicMuteButton.Background = isMuted ? MicMutedBrush : (System.Windows.Media.Brush)FindResource("NotchPanel");
    }

    private void ApplyMedia(MediaSnapshot media)
    {
        MediaPanel.Visibility = media.HasMedia ? Visibility.Visible : Visibility.Collapsed;
        NotificationList.Height = media.HasMedia ? 56 : 110;
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

        var artwork = MediaArtwork.FromBytes(media.Thumbnail);
        MediaArtworkImage.Source = artwork;
        MediaArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        MediaArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
        MediaToastArtworkImage.Source = artwork;
        MediaToastArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        MediaToastArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _animationFrameRate = DisplayRefreshRateService.GetPrimaryRefreshRate();
        ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        RefreshFocusTimer();
        await RefreshStatusAsync();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_compactToastVisible || DateTime.UtcNow < _ignoreHoverUntilUtc)
        {
            return;
        }

        _collapseTimer.Stop();
        SetExpanded(true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
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
        UpdateShelfMiniIndicator();
        _expandedReveal?.Cancel();
        _expandedReveal?.Dispose();
        _expandedReveal = null;
        if (!expanded)
        {
            ApplyShellMode(ForegroundWindowService.DetectShellMode());
            return;
        }

        ShellAnimator.Hide(DateText);
        ShellAnimator.Hide(StatusGroup);
        ClockGroup.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        ApplyHeaderDensity(isFullBar: false);
        _appBar.Release();
        SetMouseTransparent(false);
        HeaderRow.Height = new GridLength(28);
        NotchShell.Padding = new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = new CornerRadius(0, 0, 34, 34);
        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        DetailPanel.Opacity = 0;
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.Expanded(PrimaryScreenWidthDip()), _animationFrameRate);
        _ = RefreshControlCenterAsync();
        _expandedReveal = new CancellationTokenSource();
        _ = RevealExpandedContentAsync(_expandedReveal.Token);
    }

    private void ShowMediaToast()
    {
        ShowCompactToast(MediaToastPanel);
    }

    private void ShowNotificationToast(NotificationItem notification)
    {
        NotificationToastTitleText.Text = notification.Title;
        NotificationToastBodyText.Text = notification.Body;
        NotificationToastAppText.Text = notification.App;
        NotificationToastTimeText.Text = notification.TimeText;
        NotificationToastIconFallback.FontFamily = ToastTextFont;
        NotificationToastIconFallback.FontSize = 16;
        NotificationToastIconFallback.Text = notification.BadgeText;

        var icon = MediaArtwork.FromBytes(notification.Icon);
        NotificationToastIconImage.Source = icon;
        NotificationToastIconImage.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        NotificationToastIconFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;

        ApplyNotificationToastActions(notification.Actions);

        ShowCompactToast(NotificationToastPanel);
    }

    private void ShowPriorityAlertToast(PriorityStatusAlert alert)
    {
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

        ShowCompactToast(NotificationToastPanel);
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
        if (_expanded)
        {
            return;
        }

        HideCompactToast(restoreShell: false);
        _compactToastHide = new CancellationTokenSource();
        _compactToastVisible = true;
        _activeCompactToast = panel;

        _appBar.Release();
        SetMouseTransparent(false);
        ShellAnimator.Hide(ClockGroup);
        ShellAnimator.Hide(StatusGroup);
        DetailPanel.Opacity = 0;
        HeaderRow.Height = new GridLength(28);
        NotchShell.Padding = new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = new CornerRadius(0, 0, 24, 24);
        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.MediaToast(PrimaryScreenWidthDip()), _animationFrameRate);
        panel.Opacity = 0;
        ShellAnimator.Show(panel, _animationFrameRate);
        _ = HideCompactToastAfterDelayAsync(_compactToastHide.Token);
    }

    private async Task HideCompactToastAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_settings.Current.Toasts.DurationScale.ApplyTo(ShellAnimationTiming.MediaToastDuration), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        HideCompactToast(restoreShell: true);
    }

    private void HideCompactToast(bool restoreShell)
    {
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
            ShellAnimator.Hide(_activeCompactToast);
            _activeCompactToast = null;
        }

        ClockGroup.Visibility = Visibility.Visible;
        ClockGroup.Opacity = 1;
        if (restoreShell)
        {
            ApplyShellMode(ForegroundWindowService.DetectShellMode());
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

        ShellAnimator.Animate(DetailPanel, OpacityProperty, 1, _animationFrameRate);
        try
        {
            await Task.Delay(ShellAnimationTiming.MotionDuration - ShellAnimationTiming.DetailRevealDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_expanded || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        ClockGroup.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        HeaderRow.Height = new GridLength(48);
        NotchShell.Padding = new Thickness(18, 8, 18, 12);
        if (_settings.Current.General.ShowDate)
        {
            ShellAnimator.Show(DateText, _animationFrameRate);
        }
        else
        {
            ShellAnimator.Hide(DateText);
        }

        ShellAnimator.Show(StatusGroup, _animationFrameRate);
    }

    private void ApplyShellMode(ShellMode mode, bool animate = true)
    {
        if (_expanded || _compactToastVisible)
        {
            return;
        }

        _currentShellMode = mode;
        var isFullBar = mode == ShellMode.FullBar;
        var geometry = ShellMetrics.ForMode(isFullBar, PrimaryScreenWidthDip());

        ShellAnimator.Hide(DateText);
        ClockGroup.Visibility = Visibility.Visible;
        ClockGroup.Opacity = 1;
        StatusGroup.Visibility = isFullBar ? Visibility.Visible : Visibility.Collapsed;
        StatusGroup.Opacity = isFullBar ? 1 : 0;
        ApplyHeaderDensity(isFullBar);
        ClockGroup.HorizontalAlignment = isFullBar ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Center;
        HeaderRow.Height = new GridLength(isFullBar ? 28 : 28);
        NotchShell.Padding = isFullBar ? new Thickness(10, 2, 10, 2) : new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = isFullBar ? new CornerRadius(0) : new CornerRadius(0, 0, 20, 20);
        if (isFullBar)
        {
            _appBar.ReserveTop(this, geometry.WindowHeight);
        }
        else
        {
            _appBar.Release();
        }

        if (animate)
        {
            ShellAnimator.Animate(DetailPanel, OpacityProperty, 0, _animationFrameRate);
            ShellAnimator.AnimateShell(this, NotchShell, geometry, _animationFrameRate);
            SetMouseTransparent(isFullBar);
            UpdateShelfMiniIndicator();
            return;
        }

        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        DetailPanel.Opacity = 0;
        Width = geometry.Width;
        Height = geometry.WindowHeight;
        Left = geometry.Left;
        Top = 0;
        NotchShell.Width = geometry.Width;
        NotchShell.Height = geometry.ShellHeight;
        SetMouseTransparent(isFullBar);
        UpdateShelfMiniIndicator();
    }

    private void ApplyHeaderDensity(bool isFullBar)
    {
        LogoBadge.Width = isFullBar ? 24 : 28;
        LogoBadge.Height = isFullBar ? 24 : 28;
        LogoBadge.CornerRadius = new CornerRadius(isFullBar ? 12 : 14);
        TimeText.FontSize = isFullBar ? 15 : 17;

        foreach (var chip in StatusGroup.Children.OfType<System.Windows.Controls.Border>())
        {
            chip.Padding = isFullBar ? new Thickness(9, 4, 9, 4) : new Thickness(11, 7, 11, 7);
            chip.CornerRadius = new CornerRadius(isFullBar ? 13 : 17);
        }
    }

    private double PrimaryScreenWidthDip()
    {
        return SystemParameters.PrimaryScreenWidth;
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || !IsLoaded)
        {
            return;
        }

        _audio.SetVolume((float)e.NewValue);
        VolumeText.Text = $"{e.NewValue:0}%";
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

    private void AudioSessionVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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
            WifiStateText.Text = "Select a saved Wi-Fi profile first.";
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
        Dispatcher.Invoke(RefreshClipboardPanel);
    }

    private void RefreshClipboardPanel()
    {
        ClipboardPanel.SetItems(_clipboardHistory.Items);
    }

    private void ClipboardPanel_CopyRequested(object? sender, ClipboardHistoryEntry entry)
    {
        if (_clipboardHistory.CopyToClipboard(entry.Id))
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
        Dispatcher.Invoke(() =>
        {
            UpdateClock();
            ApplyCalendarSettings(settings);
            if (!_expanded || !settings.General.ShowDate)
            {
                ShellAnimator.Hide(DateText);
            }
            else
            {
                ShellAnimator.Show(DateText, _animationFrameRate);
            }
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
        DisposeFileShelf();
        _trayIcon.Dispose();
        _settings.Changed -= Settings_Changed;
        _brightnessWriter.Dispose();
        _audio.Dispose();
        _appBar.Dispose();
        _notifications.Dispose();
        _clipboardHistory.Dispose();
        _calendarTimer.Stop();
        _calendarRefresh.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        base.OnClosed(e);
    }

    private void SetMouseTransparent(bool enabled)
    {
        WindowChromeInterop.SetMouseTransparent(this, enabled);
    }

    private static System.Windows.Media.Brush FrozenBrush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
