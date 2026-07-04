using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

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
    private HwndSource? _source;
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

    public void Start(Window window)
    {
        if (_registered)
        {
            return;
        }

        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        // The notch window owns the clipboard listener; WPF delivers WM_CLIPBOARDUPDATE through this hook.
        _source = HwndSource.FromHwnd(_windowHandle) ?? PresentationSource.FromVisual(window) as HwndSource;
        if (_source is null)
        {
            return;
        }

        _source.AddHook(WndProc);
        _registered = AddClipboardFormatListener(_windowHandle);
    }

    public bool CopyToClipboard(Guid id)
    {
        var item = _store.Find(id);
        if (item is null)
        {
            return false;
        }

        try
        {
            _writing = true;
            _reader.Write(item);
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
        _coalesceTimer.Stop();
        _coalesceTimer.Tick -= OnCoalesceTimerTick;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        if (_registered && _windowHandle != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_windowHandle);
        }

        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            HandleClipboardUpdate();
        }

        return IntPtr.Zero;
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

    private async void OnCoalesceTimerTick(object? sender, EventArgs e)
    {
        _coalesceTimer.Stop();
        if (_reading || _updates.Consume() is null)
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
        }
    }

    private async Task<ClipboardHistoryEntry?> ReadWithRetryAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return _reader.Read(DateTimeOffset.Now);
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
