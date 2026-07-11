using Microsoft.UI.Dispatching;

namespace Winotch;

/// <summary>
/// Connects Winotch's UI services to the native notification-area window.
/// Keeping Win32 ownership in <see cref="NativeTrayWindow"/> avoids taking a
/// dependency on Windows Forms in the unpackaged WinUI 3 application.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly NotificationService _notifications;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly NativeTrayWindow _trayWindow;
    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public TrayIconService(MainWindow mainWindow, SettingsService settings, StartupService startup, NotificationService notifications)
    {
        _mainWindow = mainWindow;
        _settings = settings;
        _startup = startup;
        _notifications = notifications;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The tray icon must be created on the WinUI thread.");

        _trayWindow = new NativeTrayWindow(
            GetMenuState,
            () => Dispatch(OpenSettings),
            () => Dispatch(() => _mainWindow.SetNotchPaused(!_mainWindow.IsNotchPaused)),
            () => Dispatch(ToggleStartup),
            () => Dispatch(_mainWindow.ExitFromTray));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayWindow.Dispose();
        _settingsWindow?.Close();
        _settingsWindow = null;
    }

    public void OpenSettings()
    {
        if (_disposed)
        {
            return;
        }

        _mainWindow.PrepareForSettings();

        if (_settingsWindow is not null)
        {
            _settingsWindow.RestoreAndActivate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _startup, _notifications);
        _settingsWindow.Closed += SettingsWindow_Closed;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void SettingsWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (!ReferenceEquals(sender, _settingsWindow))
        {
            return;
        }

        _settingsWindow.Closed -= SettingsWindow_Closed;
        _settingsWindow = null;
    }

    private NativeTrayMenuState GetMenuState()
    {
        var startup = _startup.GetState(StartupService.CurrentExecutablePath());
        return new NativeTrayMenuState(
            _mainWindow.IsNotchPaused,
            startup.IsEnabled,
            startup.CanAccess);
    }

    private void ToggleStartup()
    {
        var current = _startup.GetState(StartupService.CurrentExecutablePath());
        if (!current.CanAccess)
        {
            return;
        }

        var updated = _startup.SetEnabled(!current.IsEnabled, StartupService.CurrentExecutablePath());
        if (updated.CanAccess)
        {
            _settings.Update(settings => settings with
            {
                General = settings.General with { StartWithWindows = updated.IsEnabled }
            });
        }
    }

    private void Dispatch(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });
    }
}
