using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace Winotch;

public sealed class TrayIconService : IDisposable
{
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private readonly MainWindow _mainWindow;
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly NotificationService _notifications;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private SettingsWindow? _settingsWindow;
    private HwndSource? _source;

    public TrayIconService(MainWindow mainWindow, SettingsService settings, StartupService startup, NotificationService notifications)
    {
        _mainWindow = mainWindow;
        _settings = settings;
        _startup = startup;
        _notifications = notifications;
        _pauseItem = new Forms.ToolStripMenuItem();
        _startupItem = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = CreateContextMenu(),
            Icon = LoadTrayIcon(),
            Text = "Winotch",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(OpenSettings);
        _mainWindow.SourceInitialized += MainWindow_SourceInitialized;
    }

    public void Dispose()
    {
        _mainWindow.SourceInitialized -= MainWindow_SourceInitialized;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _settingsWindow?.Close();
        var icon = _notifyIcon.Icon;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        icon?.Dispose();
    }

    private Forms.ContextMenuStrip CreateContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var settingsItem = new Forms.ToolStripMenuItem("Open Settings");
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        settingsItem.Click += (_, _) => Dispatch(OpenSettings);
        _pauseItem.Click += (_, _) => Dispatch(() => _mainWindow.SetNotchPaused(!_mainWindow.IsNotchPaused));
        _startupItem.Click += (_, _) => ToggleStartup();
        exitItem.Click += (_, _) => Dispatch(_mainWindow.ExitFromTray);
        menu.Opening += (_, _) => RefreshMenuState();
        menu.Items.Add(settingsItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        RefreshMenuState();
        return menu;
    }

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _startup, _notifications);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ToggleStartup()
    {
        var current = _startup.GetState(StartupService.CurrentExecutablePath());
        var updated = _startup.SetEnabled(!current.IsEnabled, StartupService.CurrentExecutablePath());
        if (updated.CanAccess)
        {
            _settings.Update(settings => settings with
            {
                General = settings.General with { StartWithWindows = updated.IsEnabled }
            });
        }

        RefreshMenuState();
    }

    private void RefreshMenuState()
    {
        _pauseItem.Text = _mainWindow.IsNotchPaused ? "Resume notch" : "Pause notch";
        var startup = _startup.GetState(StartupService.CurrentExecutablePath());
        _startupItem.Checked = startup.IsEnabled;
        _startupItem.Enabled = startup.CanAccess;
        _startupItem.ToolTipText = startup.CanAccess ? "" : startup.ErrorMessage ?? "Startup setting unavailable.";
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(_mainWindow) is HwndSource source)
        {
            _source = source;
            _source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == TaskbarCreatedMessage)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }

        return IntPtr.Zero;
    }

    private void Dispatch(Action action)
    {
        if (_mainWindow.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _mainWindow.Dispatcher.Invoke(action);
        }
    }

    private static DrawingIcon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Winotch;component/Resources/WinotchTray.ico"));
        if (resource?.Stream is null)
        {
            return (DrawingIcon)System.Drawing.SystemIcons.Application.Clone();
        }

        using var stream = resource.Stream;
        using var icon = new DrawingIcon(stream);
        return (DrawingIcon)icon.Clone();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
