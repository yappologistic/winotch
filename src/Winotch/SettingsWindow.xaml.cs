using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Notifications.Management;

namespace Winotch;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly NotificationService _notifications;
    private bool _syncing;
    private bool _centered;
    private bool _settingsFullScreen;
    private Windows.Graphics.RectInt32? _settingsRestoreBounds;
    private readonly DispatcherTimer _scrollIndicatorTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(750)
    };

    public SettingsWindow(SettingsService settings, StartupService startup, NotificationService notifications)
    {
        _settings = settings;
        _startup = startup;
        _notifications = notifications;
        _syncing = true;
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        _syncing = false;
        _scrollIndicatorTimer.Tick += (_, _) =>
        {
            _scrollIndicatorTimer.Stop();
            SettingsScrollIndicator.Opacity = 0;
        };
        Loaded += SettingsWindow_Loaded;
        Closed += SettingsWindow_Closed;
        AppWindow.Changed += SettingsAppWindow_Changed;
        _settings.Changed += Settings_Changed;
    }

    /// <summary>
    /// FluentWindow defaults to overlay chrome. Settings is a long-lived app
    /// surface, so restore the native resizable presenter after showing it.
    /// </summary>
    public new void Show()
    {
        base.Show();
        ConfigureSettingsPresenter();
        CenterOnFirstShow();
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev"}";
        SyncFromSettings(_settings.Current);
        RefreshStartupState(persist: true);
        SyncSettingsLayoutToClient();
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs e)
    {
        _scrollIndicatorTimer.Stop();
        _settings.Changed -= Settings_Changed;
    }

    private void ToggleSettingsMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (_settingsFullScreen)
        {
            RestoreSettingsBounds();
            return;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State != OverlappedPresenterState.Restored)
        {
            presenter.Restore();
        }

        _settingsRestoreBounds = new Windows.Graphics.RectInt32(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.Size.Width,
            AppWindow.Size.Height);
        _settingsFullScreen = true;
        SettingsRoot.Width = double.NaN;
        SettingsRoot.Height = double.NaN;
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        Activate();
        QueueSettingsLayoutSync();
        UpdateSettingsCaptionState();
    }

    private void RestoreSettingsBounds()
    {
        if (_settingsRestoreBounds is not { } restoreBounds)
        {
            return;
        }

        _settingsFullScreen = false;
        AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        ConfigureSettingsPresenter();
        ApplySettingsBounds(restoreBounds);
        _settingsRestoreBounds = null;
        UpdateSettingsCaptionState();
    }

    private void ApplySettingsBounds(Windows.Graphics.RectInt32 bounds)
    {
        SettingsRoot.Width = double.NaN;
        SettingsRoot.Height = double.NaN;
        AppWindow.MoveAndResize(bounds);
        QueueSettingsLayoutSync();
    }

    private void MinimizeSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsFullScreen)
        {
            RestoreSettingsBounds();
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void CloseSettingsClick(object sender, RoutedEventArgs e) => Close();

    private void SettingsAppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPresenterChange)
        {
            QueueSettingsLayoutSync();
        }
    }

    private void QueueSettingsLayoutSync() =>
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                SyncSettingsLayoutToClient();
                UpdateSettingsCaptionState();
            });

    private void SyncSettingsLayoutToClient()
    {
        SettingsRoot.Width = double.NaN;
        SettingsRoot.Height = double.NaN;
    }

    private void UpdateSettingsCaptionState()
    {
        SettingsMaximizeGlyph.Text = _settingsFullScreen
            ? "\uE923"
            : "\uE922";
    }

    private void SettingsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (SettingsScrollIndicator.Opacity <= 0 || sender is not ScrollViewer viewer)
        {
            return;
        }

        ShowSettingsScrollIndicator(viewer);
    }

    private void SettingsScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
        {
            ShowSettingsScrollIndicator(viewer);
        }
    }

    private void ShowSettingsScrollIndicator(ScrollViewer viewer)
    {
        if (viewer.ScrollableHeight <= 0 || viewer.ExtentHeight <= 0)
        {
            SettingsScrollIndicator.Opacity = 0;
            return;
        }

        var trackHeight = Math.Max(0, viewer.ActualHeight - 24);
        var thumbHeight = Math.Clamp(
            trackHeight * (viewer.ViewportHeight / viewer.ExtentHeight),
            Math.Min(36, trackHeight),
            trackHeight);
        var progress = viewer.VerticalOffset / viewer.ScrollableHeight;
        SettingsScrollIndicator.Height = thumbHeight;
        SettingsScrollIndicatorTransform.Y = Math.Max(0, (trackHeight - thumbHeight) * progress);
        SettingsScrollIndicator.Opacity = 1;
        _scrollIndicatorTimer.Stop();
        _scrollIndicatorTimer.Start();
    }

    private void Settings_Changed(object? sender, WinotchSettings settings)
    {
        _ = DispatcherQueue.TryEnqueue(() => SyncFromSettings(settings));
    }

    private void SyncFromSettings(WinotchSettings settings)
    {
        _syncing = true;
        Use24HourClockToggle.IsOn = settings.General.Use24HourClock;
        ShowDateToggle.IsOn = settings.General.ShowDate;
        StartWithWindowsToggle.IsOn = settings.General.StartWithWindows;
        MediaToastsToggle.IsOn = settings.Toasts.MediaToastsEnabled;
        NotificationToastsToggle.IsOn = settings.Toasts.NotificationToastsEnabled;
        PriorityAlertsToggle.IsOn = settings.Toasts.PriorityAlertsEnabled;
        SelectDuration(settings.Toasts.DurationScale);
        ClipboardHistoryEnabledToggle.IsOn = settings.Features.ClipboardHistoryEnabled;
        ShowAppMixerToggle.IsOn = settings.Features.ShowAppMixer;
        SystemStatsEnabledToggle.IsOn = settings.Features.SystemStatsEnabled;
        FollowActiveMonitorToggle.IsOn = settings.Features.FollowActiveMonitor;
        ActivityDotsEnabledToggle.IsOn = settings.LiveActivities.ActivityDotsEnabled;
        NowPlayingStripEnabledToggle.IsOn = settings.LiveActivities.NowPlayingStripEnabled;
        TransientTimerEnabledToggle.IsOn = settings.LiveActivities.TransientTimerEnabled;
        CallDetectionEnabledToggle.IsOn = settings.LiveActivities.CallDetectionEnabled;
        ShelfEnabledToggle.IsOn = settings.Shelf.Enabled;
        SelectShelfCap(settings.Shelf.Cap);
        ColorPickerEnabledToggle.IsOn = settings.Droplets.ColorPickerEnabled;
        TextScrubberEnabledToggle.IsOn = settings.Droplets.TextScrubberEnabled;
        CalendarEnabledToggle.IsOn = settings.Calendar.Enabled;
        CommandBarEnabledToggle.IsOn = settings.CommandBar.Enabled;
        if (!StringComparer.Ordinal.Equals(CommandBarHotkeyTextBox.Text, settings.CommandBar.Hotkey))
        {
            CommandBarHotkeyTextBox.Text = settings.CommandBar.Hotkey;
        }

        CommandBarAppsToggle.IsOn = settings.CommandBar.AppLauncherEnabled;
        CommandBarWindowsToggle.IsOn = settings.CommandBar.WindowSwitcherEnabled;
        CommandBarCalculatorToggle.IsOn = settings.CommandBar.CalculatorEnabled;
        CommandBarUnitsToggle.IsOn = settings.CommandBar.UnitConverterEnabled;
        CommandBarQuickCommandsToggle.IsOn = settings.CommandBar.QuickCommandsEnabled;
        var calendarUrls = string.Join(Environment.NewLine, settings.Calendar.SubscriptionUrls);
        if (!StringComparer.Ordinal.Equals(CalendarUrlsTextBox.Text, calendarUrls))
        {
            CalendarUrlsTextBox.Text = calendarUrls;
        }

        _syncing = false;
    }

    private void GeneralSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            General = settings.General with
            {
                Use24HourClock = Use24HourClockToggle.IsOn,
                ShowDate = ShowDateToggle.IsOn
            }
        });
    }

    private void ToastSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Toasts = settings.Toasts with
            {
                MediaToastsEnabled = MediaToastsToggle.IsOn,
                NotificationToastsEnabled = NotificationToastsToggle.IsOn,
                PriorityAlertsEnabled = PriorityAlertsToggle.IsOn
            }
        });
    }

    private void FeatureSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Features = settings.Features with
            {
                ClipboardHistoryEnabled = ClipboardHistoryEnabledToggle.IsOn,
                ShowAppMixer = ShowAppMixerToggle.IsOn,
                SystemStatsEnabled = SystemStatsEnabledToggle.IsOn,
                FollowActiveMonitor = FollowActiveMonitorToggle.IsOn
            }
        });
    }

    private void ShelfSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Shelf = settings.Shelf with
            {
                Enabled = ShelfEnabledToggle.IsOn,
                Cap = SelectedShelfCap()
            }
        });
    }

    private void DropletSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Droplets = settings.Droplets with
            {
                ColorPickerEnabled = ColorPickerEnabledToggle.IsOn,
                TextScrubberEnabled = TextScrubberEnabledToggle.IsOn
            }
        });
    }

    private void CalendarSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Calendar = settings.Calendar with { Enabled = CalendarEnabledToggle.IsOn }
        });
    }

    private void LiveActivitySettingChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            LiveActivities = settings.LiveActivities with
            {
                ActivityDotsEnabled = ActivityDotsEnabledToggle.IsOn,
                NowPlayingStripEnabled = NowPlayingStripEnabledToggle.IsOn,
                TransientTimerEnabled = TransientTimerEnabledToggle.IsOn,
                CallDetectionEnabled = CallDetectionEnabledToggle.IsOn
            }
        });
    }

    private void CalendarUrlsChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Calendar = settings.Calendar with { SubscriptionUrls = CalendarSubscriptionUrl.FromMultiline(CalendarUrlsTextBox.Text) }
        });
    }

    private void ToastDurationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(settings => settings with
        {
            Toasts = settings.Toasts with { DurationScale = SelectedDuration() }
        });
    }

    private async void RequestNotificationAccessClick(object sender, RoutedEventArgs e)
    {
        RequestNotificationAccessButton.IsEnabled = false;
        NotificationAccessStatusText.Text = "Requesting notification access...";
        NotificationAccessStatusText.Visibility = Visibility.Visible;
        try
        {
            var access = await _notifications.RequestHistoryAccessAsync();
            NotificationAccessStatusText.Text = access == UserNotificationListenerAccessStatus.Allowed
                ? "Notification history access enabled."
                : "Notification history is unavailable without package identity and permission.";
        }
        catch
        {
            NotificationAccessStatusText.Text = "Notification access request failed.";
        }
        finally
        {
            RequestNotificationAccessButton.IsEnabled = true;
        }
    }

    private async void CopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        CopyDiagnosticsButton.IsEnabled = false;
        DiagnosticsStatusText.Text = "Preparing diagnostics...";
        DiagnosticsStatusText.Visibility = Visibility.Visible;
        try
        {
            var package = new DataPackage();
            package.SetText(await DiagnosticsReport.CaptureAsync(_settings.Current, _startup));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            DiagnosticsStatusText.Text = "Diagnostics copied. Review before sharing.";
        }
        catch
        {
            DiagnosticsStatusText.Text = "Diagnostics copy failed.";
        }
        finally
        {
            CopyDiagnosticsButton.IsEnabled = true;
        }
    }

    private void StartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var state = _startup.SetEnabled(StartWithWindowsToggle.IsOn, StartupService.CurrentExecutablePath());
        if (state.CanAccess)
        {
            _settings.Update(settings => settings with
            {
                General = settings.General with { StartWithWindows = state.IsEnabled }
            });
        }

        RefreshStartupState(persist: state.CanAccess);
    }

    private void RefreshStartupState(bool persist)
    {
        var state = _startup.GetState(StartupService.CurrentExecutablePath());
        if (persist && state.CanAccess)
        {
            _settings.Update(settings => settings with
            {
                General = settings.General with { StartWithWindows = state.IsEnabled }
            });
        }

        _syncing = true;
        StartWithWindowsToggle.IsEnabled = state.CanAccess;
        StartWithWindowsToggle.IsOn = state.IsEnabled;
        StartupStatusText.Text = state.CanAccess ? "" : $"Startup setting unavailable: {state.ErrorMessage}";
        StartupStatusText.Visibility = state.CanAccess ? Visibility.Collapsed : Visibility.Visible;
        _syncing = false;
    }

    private void SelectDuration(ToastDurationScale scale)
    {
        foreach (var candidate in ToastDurationComboBox.Items)
        {
            if (candidate is ComboBoxItem item &&
                item.Tag is string text &&
                Enum.TryParse<ToastDurationScale>(text, out var itemScale) &&
                itemScale == scale)
            {
                ToastDurationComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectShelfCap(int cap)
    {
        foreach (var candidate in ShelfCapComboBox.Items)
        {
            if (candidate is ComboBoxItem item &&
                item.Tag is string text &&
                int.TryParse(text, out var itemCap) &&
                itemCap == cap)
            {
                ShelfCapComboBox.SelectedItem = item;
                return;
            }
        }

        ShelfCapComboBox.SelectedIndex = 1;
    }

    private ToastDurationScale SelectedDuration() =>
        ToastDurationComboBox.SelectedItem is ComboBoxItem { Tag: string text } &&
        Enum.TryParse<ToastDurationScale>(text, out var scale)
            ? scale
            : ToastDurationScale.Normal;

    private int SelectedShelfCap() =>
        ShelfCapComboBox.SelectedItem is ComboBoxItem { Tag: string text } && int.TryParse(text, out var cap)
            ? cap
            : 8;

    private void ConfigureSettingsPresenter()
    {
        AppWindow.Title = Title;
        AppWindow.IsShownInSwitchers = true;
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: true);
        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;
        presenter.IsAlwaysOnTop = false;
    }

    private void CenterOnFirstShow()
    {
        if (_centered)
        {
            return;
        }

        _centered = true;
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var scale = RasterizationScale;
        var widthPixels = Width * scale;
        var heightPixels = Height * scale;
        MoveToAtScale(
            (workArea.X + Math.Max(0, (workArea.Width - widthPixels) / 2)) / scale,
            (workArea.Y + Math.Max(0, (workArea.Height - heightPixels) / 2)) / scale,
            scale);
    }
}
