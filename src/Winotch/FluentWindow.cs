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
    private double _bottomCornerRadius = 38;
    private FluentWindow? _owner;
    private bool _isLoaded;
    private bool _isPointerOver;

    public FluentWindow()
    {
        Activated += (_, args) =>
        {
            _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            ConfigureDwmBorder();
            EnforceOverlayTopmost();
        };
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
            if (!UseOverlayChrome)
            {
                var scale = RasterizationScale;
                if (args.DidPositionChange)
                {
                    _left = AppWindow.Position.X / scale;
                    _top = AppWindow.Position.Y / scale;
                }

                if (args.DidSizeChange)
                {
                    _width = AppWindow.Size.Width / scale;
                    _height = AppWindow.Size.Height / scale;
                }
            }

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

    public double BottomCornerRadius
    {
        get => _bottomCornerRadius;
        set
        {
            _bottomCornerRadius = Math.Max(0, value);
            ApplyWindowRegion();
        }
    }

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
        ConfigureDwmBorder();
        EnforceOverlayTopmost();
    }

    public void ShowWithoutActivation()
    {
        InitializeFluentWindow();
        AppWindow.Show(activateWindow: false);
        _isVisible = true;
        ConfigureDwmBorder();
        EnforceOverlayTopmost();
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

    internal void MoveToAtScale(double leftDip, double topDip, double dpiScale)
    {
        _left = leftDip;
        _top = topDip;
        ApplyBounds(dpiScale);
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

    internal void MoveAndResizeAtScale(
        double leftDip,
        double topDip,
        double widthDip,
        double heightDip,
        double dpiScale)
    {
        _left = leftDip;
        _top = topDip;
        _width = Math.Max(1, widthDip);
        _height = Math.Max(1, heightDip);
        ApplyBounds(dpiScale);
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
        ConfigureDwmBorder();
        EnforceOverlayTopmost();
    }

    private void ConfigureDwmBorder()
    {
        if (!UseOverlayChrome)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Windows 11 otherwise draws a one-pixel non-client border even when
        // the title bar is disabled. COLOR_NONE removes that outer stripe.
        var color = DwmColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(uint));
    }

    private void EnforceOverlayTopmost()
    {
        if (!UseOverlayChrome || !_topmost)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero)
        {
            _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
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
            if (root.XamlRoot is not null)
            {
                root.XamlRoot.Changed += (_, _) => ApplyBounds();
            }
            ApplyBounds();
            Loaded?.Invoke(this, args);
        };
        root.PointerEntered += (_, _) => _isPointerOver = true;
        root.PointerExited += (_, _) => _isPointerOver = false;
    }

    internal double RasterizationScale
    {
        get
        {
            if (Content is FrameworkElement { XamlRoot: not null } root &&
                root.XamlRoot.RasterizationScale > 0)
            {
                return root.XamlRoot.RasterizationScale;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = hwnd == IntPtr.Zero ? 96u : GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96d : 1d;
        }
    }

    private void ApplyBounds(double? scaleOverride = null)
    {
        if (AppWindow is null)
        {
            return;
        }

        // A normal app window owns its bounds while maximized/minimized. Reapplying
        // cached XAML dimensions here would immediately undo the native presenter
        // transition when XamlRoot reports its new rasterization scale.
        if (!UseOverlayChrome &&
            AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State is OverlappedPresenterState.Maximized or OverlappedPresenterState.Minimized)
        {
            return;
        }

        var scale = scaleOverride is > 0 ? scaleOverride.Value : RasterizationScale;
        var bounds = ToPhysicalBounds(_left, _top, _width, _height, scale);
        AppWindow.MoveAndResize(bounds);
        ApplyWindowRegion(scale);
    }

    internal static RectInt32 ToPhysicalBounds(
        double leftDip,
        double topDip,
        double widthDip,
        double heightDip,
        double dpiScale)
    {
        var scale = dpiScale > 0 ? dpiScale : 1d;
        return new RectInt32(
            (int)Math.Round(leftDip * scale),
            (int)Math.Round(topDip * scale),
            Math.Max(1, (int)Math.Round(widthDip * scale)),
            Math.Max(1, (int)Math.Round(heightDip * scale)));
    }

    private void ApplyWindowRegion(double? scaleOverride = null)
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

        var scale = scaleOverride is > 0 ? scaleOverride.Value : RasterizationScale;
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
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref uint value,
        int valueSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, int wParam, IntPtr lParam);
}
