using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Winotch;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _shellTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = ShellAnimationTiming.CollapseGuard };
    private readonly AudioService _audio = new();
    private readonly WifiService _wifi = new();
    private readonly NotificationService _notifications = new();
    private readonly NotificationChangeTracker _notificationChanges = new();
    private readonly MediaService _media = new();
    private readonly MediaChangeTracker _mediaChanges = new();
    private readonly AppBarReservationService _appBar = new();
    private bool _expanded;
    private bool _updatingVolume;
    private int _animationFrameRate = 60;
    private CancellationTokenSource? _expandedReveal;

    public MainWindow()
    {
        InitializeComponent();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _shellTimer.Tick += (_, _) => ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
        _collapseTimer.Tick += (_, _) => CollapseAfterPointerExit();
        _notifications.NotificationsChanged += (_, _) => Dispatcher.Invoke(async () => await RefreshStatusAsync());
        _media.MediaChanged += (_, _) => Dispatcher.Invoke(async () => await RefreshStatusAsync());
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _animationFrameRate = DisplayRefreshRateService.GetPrimaryRefreshRate();
        ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
        UpdateClock();
        _clockTimer.Start();
        _statusTimer.Start();
        _shellTimer.Start();
        await RefreshStatusAsync();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("h:mm tt");
        DateText.Text = now.ToString("ddd, MMM d");
        LargeTimeText.Text = now.ToString("HH:mm:ss");
        LargeDateText.Text = now.ToString("dddd, MMMM d");
    }

    private async Task RefreshStatusAsync()
    {
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
        if (_mediaChanges.ShouldPop(media))
        {
            ExpandTemporarily();
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

        var notifications = await _notifications.ReadAsync();
        NotificationStateText.Text = notifications.Status;
        NotificationCountText.Text = notifications.Items.Count.ToString();
        NotificationList.ItemsSource = notifications.Items;
        if (_notificationChanges.ShouldPop(notifications.Items) && !NotificationSilenceService.IsSilenced())
        {
            ExpandTemporarily();
        }
    }

    private void ApplyMedia(MediaSnapshot media)
    {
        MediaPanel.Visibility = media.HasMedia ? Visibility.Visible : Visibility.Collapsed;
        NotificationList.Height = media.HasMedia ? 56 : 110;
        if (!media.HasMedia)
        {
            MediaArtworkImage.Source = null;
            return;
        }

        MediaTitleText.Text = media.DisplayTitle;
        MediaArtistText.Text = media.DisplayArtist;
        MediaPreviousButton.IsEnabled = media.CanPrevious;
        MediaPlayPauseButton.IsEnabled = media.IsPlaying ? media.CanPause : media.CanPlay;
        MediaNextButton.IsEnabled = media.CanNext;
        MediaPlayPauseIcon.Text = media.IsPlaying ? "\uE769" : "\uE768";

        var artwork = MediaArtwork.FromBytes(media.Thumbnail);
        MediaArtworkImage.Source = artwork;
        MediaArtworkImage.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        MediaArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _animationFrameRate = DisplayRefreshRateService.GetPrimaryRefreshRate();
        ApplyShellMode(ForegroundWindowService.DetectShellMode(), animate: false);
    }

    private async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
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

        _expanded = expanded;
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
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.Expanded(SystemParameters.PrimaryScreenWidth), _animationFrameRate);
        _expandedReveal = new CancellationTokenSource();
        _ = RevealExpandedContentAsync(_expandedReveal.Token);
    }

    private async void ExpandTemporarily()
    {
        SetExpanded(true);
        await Task.Delay(4200);
        if (!IsMouseOver)
        {
            SetExpanded(false);
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
        ShellAnimator.Show(DateText, _animationFrameRate);
        ShellAnimator.Show(StatusGroup, _animationFrameRate);
    }

    private void ApplyShellMode(ShellMode mode, bool animate = true)
    {
        if (_expanded)
        {
            return;
        }

        var isFullBar = mode == ShellMode.FullBar;
        var geometry = ShellMetrics.ForMode(isFullBar, SystemParameters.PrimaryScreenWidth);

        ShellAnimator.Hide(DateText);
        StatusGroup.Visibility = isFullBar ? Visibility.Visible : Visibility.Collapsed;
        StatusGroup.Opacity = isFullBar ? 1 : 0;
        ApplyHeaderDensity(isFullBar);
        ClockGroup.HorizontalAlignment = isFullBar ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Center;
        HeaderRow.Height = new GridLength(isFullBar ? 28 : 28);
        NotchShell.Padding = isFullBar ? new Thickness(10, 2, 10, 2) : new Thickness(10, 4, 10, 6);
        NotchShell.CornerRadius = isFullBar ? new CornerRadius(0) : new CornerRadius(0, 0, 18, 18);
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

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolume || !IsLoaded)
        {
            return;
        }

        _audio.SetVolume((float)e.NewValue);
        VolumeText.Text = $"{e.NewValue:0}%";
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

    private async Task RunMediaActionAsync(Func<Task> action)
    {
        await action();
        await RefreshStatusAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _expandedReveal?.Cancel();
        _expandedReveal?.Dispose();
        _appBar.Dispose();
        _notifications.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        base.OnClosed(e);
    }

    private void SetMouseTransparent(bool enabled)
    {
        WindowChromeInterop.SetMouseTransparent(this, enabled);
    }
}
