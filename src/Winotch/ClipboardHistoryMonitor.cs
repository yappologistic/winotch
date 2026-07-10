using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Winotch;

public sealed class ClipboardHistoryMonitor : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(45);

    private readonly ClipboardHistoryStore _store = new();
    private readonly ClipboardUpdateQueue _updates = new();
    private readonly WindowsClipboardContentReader _reader = new();
    private readonly DispatcherTimer _coalesceTimer;
    private WindowMessageSink? _messageSink;
    private IntPtr _windowHandle;
    private bool _registered;
    private bool _reading;
    private bool _writing;

    public ClipboardHistoryMonitor()
    {
        _coalesceTimer = new DispatcherTimer { Interval = CoalesceDelay };
        _coalesceTimer.Tick += OnCoalesceTimerTick;
    }

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ClipboardHistoryEntry> Items => _store.Items;

    public void Start(FluentWindow window)
    {
        if (_registered)
        {
            return;
        }

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // WinUI does not expose arbitrary HWND messages, so the notch owns a
            // scoped native subclass for the clipboard listener notification.
            _messageSink = new WindowMessageSink(_windowHandle, HandleWindowMessage);
            _registered = AddClipboardFormatListener(_windowHandle);
            if (!_registered)
            {
                _messageSink.Dispose();
                _messageSink = null;
                _windowHandle = IntPtr.Zero;
            }
        }
        catch
        {
            _messageSink?.Dispose();
            _messageSink = null;
            _windowHandle = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        _coalesceTimer.Stop();
        if (_registered && _windowHandle != IntPtr.Zero)
        {
            _ = RemoveClipboardFormatListener(_windowHandle);
        }

        _messageSink?.Dispose();
        _messageSink = null;
        _registered = false;
        _windowHandle = IntPtr.Zero;
    }

    public async Task<bool> CopyToClipboardAsync(Guid id)
    {
        var item = _store.Find(id);
        if (item is null)
        {
            return false;
        }

        try
        {
            _writing = true;
            await _reader.WriteAsync(item);
            _updates.IgnoreSequence(GetClipboardSequenceNumber());
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _writing = false;
        }
    }

    public void Delete(Guid id)
    {
        if (_store.Delete(id))
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_store.Clear())
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Stop();
        _coalesceTimer.Tick -= OnCoalesceTimerTick;
    }

    private bool HandleWindowMessage(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message != WmClipboardUpdate)
        {
            return false;
        }

        HandleClipboardUpdate();
        // Match the old HwndSource hook's unhandled result so WinUI and any
        // other native subclasses can continue processing the notification.
        return false;
    }

    private void HandleClipboardUpdate()
    {
        if (_writing)
        {
            return;
        }

        if (_updates.Enqueue(GetClipboardSequenceNumber()))
        {
            // Clipboard owners often publish several formats in a burst; read once after the burst settles.
            _coalesceTimer.Stop();
            _coalesceTimer.Start();
        }
    }

    private async void OnCoalesceTimerTick(object? sender, object e)
    {
        _coalesceTimer.Stop();
        if (_reading)
        {
            return;
        }

        if (_updates.Consume() is null)
        {
            return;
        }

        _reading = true;
        try
        {
            var item = await ReadWithRetryAsync();
            if (item is not null)
            {
                _store.Push(item);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _reading = false;
            if (_updates.HasPending)
            {
                _coalesceTimer.Start();
            }
        }
    }

    private async Task<ClipboardHistoryEntry?> ReadWithRetryAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await _reader.ReadAsync(DateTimeOffset.Now);
            }
            catch
            {
                // Another process can hold the clipboard briefly; skip after a few short, UI-friendly retries.
                if (attempt == 2)
                {
                    return null;
                }

                await Task.Delay(RetryDelay);
            }
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
