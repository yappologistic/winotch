using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Winotch;

/// <summary>
/// Small WinUI 3 window host that centralizes the overlay behavior shared by the
/// notch and its auxiliary surfaces. AppWindow coordinates are physical pixels;
/// public geometry remains in XAML DIPs to keep existing monitor math stable.
/// </summary>
public class FluentWindow : Window
{
    private double _width = 1;
    private double _height = 1;
    private double _left;
    private double _top;
    private bool _isVisible;
    private bool _isActive;
    private bool _topmost = true;
    private bool _showInTaskbar;
    private bool _contentHooked;
    private FluentWindow? _owner;
    private bool _isLoaded;
    private bool _isPointerOver;

    public FluentWindow()
    {
        Activated += (_, args) =>
            _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        Closed += (_, _) =>
        {
            _isVisible = false;
            OnClosed(EventArgs.Empty);
        };
        AppWindow.Closing += (_, args) =>
        {
            var closingArgs = new CancelEventArgs();
            OnClosing(closingArgs);
            args.Cancel = closingArgs.Cancel;
        };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange)
            {
                ApplyWindowRegion();
            }
        };
    }

    public double Width
    {
        get => _width;
        set
        {
            _width = Math.Max(1, value);
            ApplyBounds();
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            _height = Math.Max(1, value);
            ApplyBounds();
        }
    }

    public double Left
    {
        get => _left;
        set
        {
            _left = value;
            ApplyBounds();
        }
    }

    public double Top
    {
        get => _top;
        set
        {
            _top = value;
            ApplyBounds();
        }
    }

    public bool IsVisible => _isVisible;

    public bool IsActive => _isActive;

    public bool IsLoaded => _isLoaded;

    public bool IsMouseOver => _isPointerOver;

    public FluentWindow? Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            ApplyOwner();
        }
    }

    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            _showInTaskbar = value;
            AppWindow.IsShownInSwitchers = value;
        }
    }

    public bool Topmost
    {
        get => _topmost;
        set
        {
            _topmost = value;
            ConfigurePresenter();
        }
    }

    public double BottomCornerRadius { get; set; } = 38;

    public bool AttachToTopEdge { get; set; } = true;

    public bool UseWindowRegion { get; set; } = true;

    public bool UseOverlayChrome { get; set; } = true;

    public Brush? Background { get; set; }

    /// <summary>
    /// Raised when the XAML content is loaded. WinUI Window itself is not a
    /// FrameworkElement, so this mirrors WPF's Window.Loaded seam for the port.
    /// </summary>
    public event RoutedEventHandler? Loaded;

    protected virtual void OnClosing(CancelEventArgs args)
    {
    }

    protected virtual void OnClosed(EventArgs args)
    {
    }

    protected void InitializeFluentWindow()
    {
        ConfigurePresenter();
        AppWindow.IsShownInSwitchers = _showInTaskbar;
        ApplyOwner();
        HookContent();
        ApplyBounds();
    }

    public void Show()
    {
        InitializeFluentWindow();
        AppWindow.Show();
        _isVisible = true;
    }

    public void ShowWithoutActivation()
    {
        InitializeFluentWindow();
        AppWindow.Show(activateWindow: false);
        _isVisible = true;
    }

    public void Hide()
    {
        AppWindow.Hide();
        _isVisible = false;
    }

    public void MoveTo(double leftDip, double topDip)
    {
        _left = leftDip;
        _top = topDip;
        ApplyBounds();
    }

    public void ResizeTo(double widthDip, double heightDip)
    {
        _width = Math.Max(1, widthDip);
        _height = Math.Max(1, heightDip);
        ApplyBounds();
    }

    public void MoveAndResize(double leftDip, double topDip, double widthDip, double heightDip)
    {
        _left = leftDip;
        _top = topDip;
        _width = Math.Max(1, widthDip);
        _height = Math.Max(1, heightDip);
        ApplyBounds();
    }

    public void DragMove()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WmNcLButtonDown, HtCaption, IntPtr.Zero);
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            presenter = (OverlappedPresenter)AppWindow.Presenter;
        }

        presenter.SetBorderAndTitleBar(
            hasBorder: !UseOverlayChrome,
            hasTitleBar: !UseOverlayChrome);
        presenter.IsResizable = !UseOverlayChrome;
        presenter.IsMaximizable = !UseOverlayChrome;
        presenter.IsMinimizable = !UseOverlayChrome;
        presenter.IsAlwaysOnTop = UseOverlayChrome && _topmost;
    }

    private void HookContent()
    {
        if (_contentHooked || Content is not FrameworkElement root)
        {
            return;
        }

        _contentHooked = true;
        root.Loaded += (_, args) =>
        {
            _isLoaded = true;
            ApplyBounds();
            Loaded?.Invoke(this, args);
        };
        root.PointerEntered += (_, _) => _isPointerOver = true;
        root.PointerExited += (_, _) => _isPointerOver = false;
        root.XamlRoot.Changed += (_, _) => ApplyBounds();
    }

    private double RasterizationScale =>
        Content is FrameworkElement { XamlRoot: not null } root
            ? root.XamlRoot.RasterizationScale
            : 1d;

    private void ApplyBounds()
    {
        if (AppWindow is null)
        {
            return;
        }

        var scale = RasterizationScale;
        var bounds = new RectInt32(
            (int)Math.Round(_left * scale),
            (int)Math.Round(_top * scale),
            Math.Max(1, (int)Math.Round(_width * scale)),
            Math.Max(1, (int)Math.Round(_height * scale)));
        AppWindow.MoveAndResize(bounds);
        ApplyWindowRegion();
    }

    private void ApplyWindowRegion()
    {
        if (!UseWindowRegion)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var scale = RasterizationScale;
        var width = Math.Max(1, (int)Math.Round(_width * scale));
        var height = Math.Max(1, (int)Math.Round(_height * scale));
        var radius = Math.Max(1, (int)Math.Round(BottomCornerRadius * scale));

        // Offset the top of the rounded rectangle above the HWND. This keeps the
        // notch flush/square at the monitor edge while rounding only its bottom.
        var regionTop = AttachToTopEdge ? -radius : 0;
        var region = CreateRoundRectRgn(0, regionTop, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(hwnd, region, redraw: true) == 0)
        {
            _ = DeleteObject(region);
        }
        // On success Windows owns the HRGN and releases it with the window region.
    }

    private void ApplyOwner()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ownerHwnd = _owner is null
            ? IntPtr.Zero
            : WinRT.Interop.WindowNative.GetWindowHandle(_owner);
        _ = SetWindowLongPtr(hwnd, GwlHwndParent, ownerHwnd);
    }

    private const int GwlHwndParent = -8;
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, int wParam, IntPtr lParam);
}
