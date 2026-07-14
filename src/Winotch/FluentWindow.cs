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
    private ShellGeometry? _visibleShellRegion;
    private ShellGeometry? _visibleShellHost;

    public FluentWindow()
    {
        ApplyWindowIcon();
        Activated += (_, args) =>
        {
            _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            NormalizeOverlayWindowStyle();
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

    private void ApplyWindowIcon()
    {
        var iconPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "WinotchTray.ico"),
            Path.Combine(AppContext.BaseDirectory, "WinotchTray.ico")
        };

        var iconPath = iconPaths.FirstOrDefault(File.Exists);
        if (iconPath is not null)
        {
            AppWindow.SetIcon(iconPath);
        }
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

    public double MinimumShellHostWidth { get; set; }

    public double MinimumShellHostHeight { get; set; }

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
        NormalizeOverlayWindowStyle();
        ConfigureDwmBorder();
        EnforceOverlayTopmost();
    }

    public void ShowWithoutActivation()
    {
        InitializeFluentWindow();
        AppWindow.Show(activateWindow: false);
        _isVisible = true;
        NormalizeOverlayWindowStyle();
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
        NormalizeOverlayWindowStyle();
        ConfigureDwmBorder();
        EnforceOverlayTopmost();
    }

    private void NormalizeOverlayWindowStyle()
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

        // AppWindow's borderless presenter can retain a DLGFRAME/SYSMENU style
        // on some Windows builds. That leaves a non-client inset around every
        // shell size even when DWMWA_BORDER_COLOR is unavailable. Normalize the
        // overlay to a true popup so its client area equals its window bounds.
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var next = (style & ~OverlayChromeStyleMask) | WsPopup;
        if (next == style)
        {
            return;
        }

        _ = SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(next));
        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void ConfigureDwmBorder()
    {
        if (!UseOverlayChrome)
        {
            return;
        }

        SuppressDwmNonClientEffects();
    }

    protected void SuppressDwmNonClientEffects()
    {

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // The shell owns its rounded HRGN and composition motion. Disable DWM's
        // independent non-client rounding, transition, and shadow so it cannot
        // leave a second lower plate behind the acrylic surface.
        var disabled = 1u;
        _ = DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref disabled, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaTransitionsForcedDisabled, ref disabled, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref disabled, sizeof(uint));

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
                root.XamlRoot.Changed += (_, _) =>
                {
                    // Overlay coordinates are DIP-based and must be reapplied
                    // when monitor scale changes. Conventional windows instead
                    // remain fully owned by the native presenter so maximize,
                    // snap, and restore transitions cannot be overwritten.
                    if (UseOverlayChrome)
                    {
                        ApplyBounds();
                    }
                };
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

    internal (double Width, double Height) GetClientSizeInDips()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = RasterizationScale;
        if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out var clientRect))
        {
            return (
                Math.Max(1, (clientRect.Right - clientRect.Left) / scale),
                Math.Max(1, (clientRect.Bottom - clientRect.Top) / scale));
        }

        return (_width, _height);
    }

    internal void FlushDwmComposition() => _ = DwmFlush();

    internal ShellGeometry ResolveShellHostGeometry(ShellGeometry host) =>
        ShellMetrics.ExpandHost(host, MinimumShellHostWidth, MinimumShellHostHeight);

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

        if (_visibleShellRegion is { } shell && _visibleShellHost is { } host)
        {
            ApplyVisibleShellRegionCore(shell, host);
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
        ApplyCreatedWindowRegion(hwnd, region);
    }

    internal void ApplyVisibleShellRegion(ShellGeometry shell, ShellGeometry host)
    {
        if (!UseWindowRegion || !UseOverlayChrome)
        {
            return;
        }

        _visibleShellRegion = shell;
        _visibleShellHost = host;
        ApplyVisibleShellRegionCore(shell, host);
    }

    /// <summary>
    /// Commits the clipped shell region before the backing HWND grows. AppWindow
    /// size notifications can then only reapply the clipped region, never the
    /// temporarily larger union host used by the composition animation.
    /// </summary>
    internal void PrepareVisibleShellRegion(ShellGeometry shell, ShellGeometry host)
    {
        _visibleShellRegion = shell;
        _visibleShellHost = host;
        ApplyVisibleShellRegionCore(shell, host);
    }

    private void ApplyVisibleShellRegionCore(ShellGeometry shell, ShellGeometry host)
    {
        if (!UseWindowRegion || !UseOverlayChrome)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var scale = host.DpiScale > 0 ? host.DpiScale : RasterizationScale;
        var left = (int)Math.Round((shell.Left - host.Left) * scale);
        var top = (int)Math.Round((shell.Top - host.Top) * scale);
        var width = Math.Max(1, (int)Math.Round(shell.Width * scale));
        var height = Math.Max(1, (int)Math.Round(shell.WindowHeight * scale));
        var radius = Math.Max(1, (int)Math.Round(BottomCornerRadius * scale));
        var regionTop = AttachToTopEdge ? top - radius : top;
        var region = CreateRoundRectRgn(
            left,
            regionTop,
            left + width + 1,
            top + height + 1,
            radius * 2,
            radius * 2);
        ApplyCreatedWindowRegion(hwnd, region);
    }

    private static void ApplyCreatedWindowRegion(IntPtr hwnd, IntPtr region)
    {
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
    private const int GwlStyle = -16;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaTransitionsForcedDisabled = 3;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const long WsBorder = 0x00800000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsPopup = unchecked((long)0x80000000L);
    private const long OverlayChromeStyleMask =
        WsBorder | WsDlgFrame | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, int wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
