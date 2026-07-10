using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Winotch.CommandBar;

namespace Winotch;

public partial class MainWindow
{
    private const int CommandHotkeyId = 0x5743;
    private const int WmHotkey = 0x0312;
    private CommandBarService? _commandBar;
    private CancellationTokenSource? _commandBarQuery;
    private WindowMessageSink? _commandBarMessageSink;
    private IntPtr _commandBarHwnd;
    private CommandHotkey? _registeredCommandHotkey;
    private bool _commandBarVisible;

    private void InitializeCommandBar()
    {
        _commandBar = new CommandBarService(
            [
                new AppLaunchProvider(),
                new WindowSwitchProvider(),
                new CalculatorProvider(),
                new UnitConverterProvider(),
                new QuickCommandProvider(CreateQuickCommands())
            ],
            () => _settings.Current.CommandBar);
        CommandBarPanel.InputBox.TextChanged += async (_, _) => await RefreshCommandBarResultsAsync();
        CommandBarPanel.InputBox.KeyDown += CommandBarInput_PreviewKeyDown;
    }

    private IReadOnlyList<QuickCommandAction> CreateQuickCommands() =>
    [
        new("mute", "Mute system audio", _ => RunQuickCommandAsync(() => _audio.SetMuted(true))),
        new("unmute", "Unmute system audio", _ => RunQuickCommandAsync(() => _audio.SetMuted(false))),
        new("wi-fi on", "Enable Wi-Fi adapter", token => _wifi.SetRadioEnabledAsync(true).WaitAsync(token)),
        new("wi-fi off", "Disable Wi-Fi adapter", token => _wifi.SetRadioEnabledAsync(false).WaitAsync(token)),
        new("night light on", "Open Windows Night light settings", _ => OpenNightLightSettingsAsync()),
        new("night light off", "Open Windows Night light settings", _ => OpenNightLightSettingsAsync()),
        new("focus start 25", "Start a 25 minute focus timer", _ => StartFocusCommandAsync(FocusTimerSettings.ShortPreset)),
        new("focus start 50", "Start a 50 minute focus timer", _ => StartFocusCommandAsync(FocusTimerSettings.LongPreset)),
        new("focus stop", "Stop the focus timer", _ => RunQuickCommandAsync(StopFocusTimerCommand)),
        new("pause notch", "Hide and pause Winotch", _ => RunQuickCommandAsync(() => SetNotchPaused(true))),
        new("resume notch", "Resume Winotch", _ => RunQuickCommandAsync(() => SetNotchPaused(false)))
    ];

    private void RegisterCommandBarHotkey()
    {
        if (!IsLoaded)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_commandBarHwnd != hwnd)
        {
            _commandBarMessageSink?.Dispose();
            _commandBarHwnd = hwnd;
            _commandBarMessageSink = new WindowMessageSink(hwnd, CommandBarWndProc);
        }

        ApplyCommandBarSettings(_settings.Current);
    }

    private void ApplyCommandBarSettings(WinotchSettings settings)
    {
        UnregisterCommandBarHotkey();
        if (!settings.CommandBar.Enabled ||
            _commandBarMessageSink is null ||
            !CommandHotkeyParser.TryParse(settings.CommandBar.Hotkey, out var hotkey))
        {
            return;
        }

        if (RegisterHotKey(_commandBarHwnd, CommandHotkeyId, hotkey.RegisterModifiers, hotkey.VirtualKey))
        {
            _registeredCommandHotkey = hotkey;
        }
    }

    private void UnregisterCommandBarHotkey()
    {
        if (_commandBarMessageSink is not null && _registeredCommandHotkey is not null)
        {
            UnregisterHotKey(_commandBarHwnd, CommandHotkeyId);
            _registeredCommandHotkey = null;
        }
    }

    private bool CommandBarWndProc(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message == WmHotkey && unchecked((int)wParam.ToUInt64()) == CommandHotkeyId)
        {
            _ = DispatcherQueue.TryEnqueue(ShowCommandBar);
            return true;
        }

        return false;
    }

    private void ShowCommandBar()
    {
        if (!_settings.Current.CommandBar.Enabled || _commandBarVisible)
        {
            return;
        }

        HideCompactToast(restoreShell: false);
        if (_expanded)
        {
            SetExpanded(false);
        }

        _collapseTimer.Stop();
        _commandBarVisible = true;
        _currentShellMode = ShellMode.Command;
        _appBar.Release();
        SetMouseTransparent(false);
        ShellAnimator.Hide(ClockGroup, _animationFrameRate);
        ShellAnimator.Hide(StatusGroup, _animationFrameRate);
        ShellAnimator.Hide(DateText, _animationFrameRate);
        ShellAnimator.Hide(LiveStrip, _animationFrameRate);
        ShellAnimator.Hide(MediaToastPanel, _animationFrameRate);
        ShellAnimator.Hide(NotificationToastPanel, _animationFrameRate);
        DetailPanel.Opacity = 0;
        CommandBarPanel.Clear();
        CommandBarPanel.Opacity = 0;
        CommandBarPanel.Visibility = Visibility.Visible;
        HeaderRow.Height = new GridLength(44);
        ShellContent.Margin = new Thickness(16, 12, 16, 20);
        SetShellCornerRadius(34);
        ShellAnimator.Clear(this, NotchShell, DetailPanel);
        var monitor = CurrentMonitor(preferCursor: true);
        ShellAnimator.AnimateShell(this, NotchShell, ShellMetrics.PlaceOnMonitor(ShellMetrics.Command(monitor.WidthDip), monitor), _animationFrameRate);
        ShellAnimator.Show(CommandBarPanel, _animationFrameRate);
        _ = RefreshCommandBarResultsAsync();
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            FocusCommandBarInput);
    }

    private void FocusCommandBarInput()
    {
        Activate();
        CommandBarPanel.FocusInput();
    }

    private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_commandBarVisible && e.Key == VirtualKey.Escape)
        {
            HideCommandBar(restoreShell: true);
            e.Handled = true;
        }
    }

    private void HideCommandBar(bool restoreShell)
    {
        if (!_commandBarVisible)
        {
            return;
        }

        _commandBarVisible = false;
        _commandBarQuery?.Cancel();
        _commandBarQuery?.Dispose();
        _commandBarQuery = null;
        ShellAnimator.Hide(CommandBarPanel, _animationFrameRate);
        ClockGroup.Visibility = Visibility.Visible;
        ClockGroup.Opacity = 1;
        _ignoreHoverUntilUtc = DateTime.UtcNow + ShellAnimationTiming.CollapseGuard;
        if (restoreShell)
        {
            ApplyForegroundState(ForegroundWindowService.DetectForeground(), animate: true, force: true);
        }
    }

    private async Task RefreshCommandBarResultsAsync()
    {
        if (!_commandBarVisible || _commandBar is null)
        {
            return;
        }

        _commandBarQuery?.Cancel();
        _commandBarQuery?.Dispose();
        var query = new CancellationTokenSource();
        _commandBarQuery = query;
        var results = await _commandBar.QueryAsync(CommandBarPanel.InputBox.Text, query.Token);
        if (!query.IsCancellationRequested)
        {
            CommandBarPanel.SetResults(results);
            ResizeCommandBarForResults(results.Count);
        }
    }

    private void ResizeCommandBarForResults(int resultCount)
    {
        if (!_commandBarVisible)
        {
            return;
        }

        var monitor = CurrentMonitor(preferCursor: true);
        var geometry = ShellMetrics.PlaceOnMonitor(
            ShellMetrics.Command(monitor.WidthDip, resultCount),
            monitor);
        ShellAnimator.AnimateShell(this, NotchShell, geometry, _animationFrameRate);
    }

    private async void CommandBarInput_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HideCommandBar(restoreShell: true);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Down)
        {
            CommandBarPanel.SelectNext(1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Up)
        {
            CommandBarPanel.SelectNext(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter && CommandBarPanel.SelectedResult is { } result)
        {
            e.Handled = true;
            await result.ExecuteAsync(CancellationToken.None);
            HideCommandBar(restoreShell: true);
            await RefreshStatusAsync();
        }
    }

    private Task StartFocusCommandAsync(FocusTimerSettings settings) =>
        RunQuickCommandAsync(() =>
        {
            _focusTimer = FocusTimerState.Start(settings, DateTimeOffset.UtcNow);
            _focusTimerStore.Save(_focusTimer);
            ApplyFocusTimerUi(DateTimeOffset.UtcNow);
        });

    private void StopFocusTimerCommand()
    {
        _focusTimer = FocusTimerState.Stopped;
        _focusTimerStore.Clear();
        ApplyFocusTimerUi(DateTimeOffset.UtcNow);
    }

    private static Task RunQuickCommandAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static Task OpenNightLightSettingsAsync()
    {
        Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void DisposeCommandBar()
    {
        UnregisterCommandBarHotkey();
        _commandBarMessageSink?.Dispose();
        _commandBarMessageSink = null;
        _commandBarHwnd = IntPtr.Zero;
        _commandBarQuery?.Cancel();
        _commandBarQuery?.Dispose();
        _commandBarQuery = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
