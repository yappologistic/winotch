using System.Windows;
using System.Windows.Controls;

namespace Winotch;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private bool _syncing;

    public SettingsWindow(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
        Closed += SettingsWindow_Closed;
        _settings.Changed += Settings_Changed;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev"}";
        SyncFromSettings(_settings.Current);
        RefreshStartupState(persist: true);
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _settings.Changed -= Settings_Changed;
    }

    private void Settings_Changed(object? sender, WinotchSettings settings)
    {
        Dispatcher.Invoke(() => SyncFromSettings(settings));
    }

    private void SyncFromSettings(WinotchSettings settings)
    {
        _syncing = true;
        Use24HourClockToggle.IsChecked = settings.General.Use24HourClock;
        ShowDateToggle.IsChecked = settings.General.ShowDate;
        StartWithWindowsToggle.IsChecked = settings.General.StartWithWindows;
        MediaToastsToggle.IsChecked = settings.Toasts.MediaToastsEnabled;
        NotificationToastsToggle.IsChecked = settings.Toasts.NotificationToastsEnabled;
        PriorityAlertsToggle.IsChecked = settings.Toasts.PriorityAlertsEnabled;
        SelectDuration(settings.Toasts.DurationScale);
        CalendarEnabledToggle.IsChecked = settings.Calendar.Enabled;
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
                Use24HourClock = Use24HourClockToggle.IsChecked == true,
                ShowDate = ShowDateToggle.IsChecked == true
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
                MediaToastsEnabled = MediaToastsToggle.IsChecked == true,
                NotificationToastsEnabled = NotificationToastsToggle.IsChecked == true,
                PriorityAlertsEnabled = PriorityAlertsToggle.IsChecked == true
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
            Calendar = settings.Calendar with { Enabled = CalendarEnabledToggle.IsChecked == true }
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

    private void StartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var state = _startup.SetEnabled(StartWithWindowsToggle.IsChecked == true, StartupService.CurrentExecutablePath());
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
        StartWithWindowsToggle.IsChecked = state.IsEnabled;
        StartupStatusText.Text = state.CanAccess ? "" : $"Startup setting unavailable: {state.ErrorMessage}";
        StartupStatusText.Visibility = state.CanAccess ? Visibility.Collapsed : Visibility.Visible;
        _syncing = false;
    }

    private void SelectDuration(ToastDurationScale scale)
    {
        foreach (ComboBoxItem item in ToastDurationComboBox.Items)
        {
            if (item.Tag is ToastDurationScale itemScale && itemScale == scale)
            {
                ToastDurationComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private ToastDurationScale SelectedDuration() =>
        ToastDurationComboBox.SelectedItem is ComboBoxItem { Tag: ToastDurationScale scale }
            ? scale
            : ToastDurationScale.Normal;
}
