using System.ComponentModel;
using System.Buffers.Binary;
using System.Resources;
using System.Runtime.InteropServices;

namespace Winotch;

internal readonly record struct NativeTrayMenuState(
    bool IsPaused,
    bool IsStartupEnabled,
    bool CanChangeStartup);

/// <summary>
/// Owns the hidden Win32 window, notification icon, and shortcut menu used by
/// Winotch. The window is an invisible top-level tool window so it receives the
/// TaskbarCreated broadcast after Explorer restarts.
/// </summary>
internal sealed class NativeTrayWindow : IDisposable
{
    private const string WindowClassName = "Winotch.NativeTrayWindow";
    private const string TrayTooltip = "Winotch";
    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = WmApp + 1;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetFocus = 0x00000003;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;

    private const uint WmApp = 0x8000;
    private const uint WmNull = 0x0000;
    private const uint WmDestroy = 0x0002;
    private const uint WmNcCreate = 0x0081;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinSelect = WmUser;
    private const uint NinKeySelect = WmUser + 1;
    private const uint WmUser = 0x0400;

    private const int GwlpUserData = -21;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsPopup = 0x80000000;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint IdiApplication = 32512;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfGrayed = 0x00000001;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const uint OpenSettingsCommand = 1001;
    private const uint TogglePauseCommand = 1002;
    private const uint ToggleStartupCommand = 1003;
    private const uint ExitCommand = 1004;

    private static readonly object WindowClassLock = new();
    private static readonly WindowProcedure SharedWindowProcedure = WindowProc;
    private static bool _windowClassRegistered;

    private readonly Func<NativeTrayMenuState> _getMenuState;
    private readonly Action _openSettings;
    private readonly Action _togglePaused;
    private readonly Action _toggleStartup;
    private readonly Action _exit;
    private readonly uint _taskbarCreatedMessage;
    private GCHandle _selfHandle;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _ownsIcon;
    private bool _iconAdded;
    private bool _disposed;

    public NativeTrayWindow(
        Func<NativeTrayMenuState> getMenuState,
        Action openSettings,
        Action togglePaused,
        Action toggleStartup,
        Action exit)
    {
        _getMenuState = getMenuState;
        _openSettings = openSettings;
        _togglePaused = togglePaused;
        _toggleStartup = toggleStartup;
        _exit = exit;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        EnsureWindowClass();
        _selfHandle = GCHandle.Alloc(this);
        _windowHandle = CreateWindowEx(
            WsExToolWindow | WsExNoActivate,
            WindowClassName,
            WindowClassName,
            WsPopup,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            GCHandle.ToIntPtr(_selfHandle));

        if (_windowHandle == IntPtr.Zero)
        {
            _selfHandle.Free();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the tray message window.");
        }

        _iconHandle = LoadTrayIcon(out _ownsIcon);
        AddIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveIcon();

        if (_windowHandle != IntPtr.Zero)
        {
            _ = DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_ownsIcon && _iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
        }

        _iconHandle = IntPtr.Zero;
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void AddIcon()
    {
        if (_disposed || _windowHandle == IntPtr.Zero || _iconHandle == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _iconAdded = ShellNotifyIcon(NimAdd, ref data);
        if (!_iconAdded)
        {
            return;
        }

        data.UnionTimeoutOrVersion = NotifyIconVersion4;
        _ = ShellNotifyIcon(NimSetVersion, ref data);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _ = ShellNotifyIcon(NimDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        IconId = TrayIconId,
        Flags = NifMessage | NifIcon | NifTip | NifShowTip,
        CallbackMessage = TrayCallbackMessage,
        IconHandle = _iconHandle,
        Tip = TrayTooltip,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private IntPtr HandleMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();
            return IntPtr.Zero;
        }

        if (message == TrayCallbackMessage)
        {
            var notification = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            switch (notification)
            {
                case NinSelect:
                case NinKeySelect:
                case WmLButtonUp:
                    _openSettings();
                    break;
                case WmContextMenu:
                case WmRButtonUp:
                    ShowContextMenu();
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            _iconAdded = false;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var state = _getMenuState();
            _ = AppendMenu(menu, MfString, OpenSettingsCommand, "Open Settings");
            _ = AppendMenu(menu, MfString, TogglePauseCommand, state.IsPaused ? "Resume notch" : "Pause notch");

            var startupFlags = MfString;
            if (state.IsStartupEnabled)
            {
                startupFlags |= MfChecked;
            }

            if (!state.CanChangeStartup)
            {
                startupFlags |= MfGrayed;
            }

            _ = AppendMenu(menu, startupFlags, ToggleStartupCommand, "Start with Windows");
            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, ExitCommand, "Exit");

            if (!GetCursorPos(out var cursor))
            {
                cursor = default;
            }

            // TrackPopupMenu requires its owner to be foreground or it may not
            // dismiss when the user clicks elsewhere.
            _ = SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCmd,
                cursor.X,
                cursor.Y,
                _windowHandle,
                IntPtr.Zero);
            _ = PostMessage(_windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
            InvokeCommand(command);

            var data = CreateNotifyIconData();
            _ = ShellNotifyIcon(NimSetFocus, ref data);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void InvokeCommand(uint command)
    {
        switch (command)
        {
            case OpenSettingsCommand:
                _openSettings();
                break;
            case TogglePauseCommand:
                _togglePaused();
                break;
            case ToggleStartupCommand:
                _toggleStartup();
                break;
            case ExitCommand:
                _exit();
                break;
        }
    }

    private static IntPtr LoadTrayIcon(out bool ownsIcon)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "WinotchTray.ico"),
            Path.Combine(AppContext.BaseDirectory, "WinotchTray.ico")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var icon = LoadImage(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile);
            if (icon != IntPtr.Zero)
            {
                ownsIcon = true;
                return icon;
            }
        }

        var embeddedIcon = LoadEmbeddedTrayIcon();
        if (embeddedIcon != IntPtr.Zero)
        {
            ownsIcon = true;
            return embeddedIcon;
        }

        ownsIcon = false;
        return LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);
    }

    private static IntPtr LoadEmbeddedTrayIcon()
    {
        try
        {
            var assembly = typeof(NativeTrayWindow).Assembly;
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var manifestStream = assembly.GetManifestResourceStream(resourceName);
                if (manifestStream is null)
                {
                    continue;
                }

                using var resources = new ResourceReader(manifestStream);
                var entries = resources.GetEnumerator();
                while (entries.MoveNext())
                {
                    if (entries.Key is not string key ||
                        !key.EndsWith("resources/winotchtray.ico", StringComparison.OrdinalIgnoreCase) ||
                        entries.Value is not Stream iconStream)
                    {
                        continue;
                    }

                    using var buffer = new MemoryStream();
                    iconStream.CopyTo(buffer);
                    return CreateIconFromIco(buffer.ToArray());
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        {
            // A malformed or unavailable optional brand resource falls through
            // to the system application icon below.
        }

        return IntPtr.Zero;
    }

    private static IntPtr CreateIconFromIco(byte[] ico)
    {
        const int headerSize = 6;
        const int entrySize = 16;
        if (ico.Length < headerSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0, 2)) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2, 2)) != 1)
        {
            return IntPtr.Zero;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2));
        if (count == 0 || ico.Length < headerSize + (count * entrySize))
        {
            return IntPtr.Zero;
        }

        var bestEntryOffset = -1;
        var bestArea = -1;
        for (var index = 0; index < count; index++)
        {
            var entryOffset = headerSize + (index * entrySize);
            var width = ico[entryOffset] == 0 ? 256 : ico[entryOffset];
            var height = ico[entryOffset + 1] == 0 ? 256 : ico[entryOffset + 1];
            var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entryOffset + 8, 4));
            var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entryOffset + 12, 4));
            if (imageLength == 0 || imageOffset > int.MaxValue || imageLength > int.MaxValue ||
                (ulong)imageOffset + imageLength > (ulong)ico.Length)
            {
                continue;
            }

            var area = width * height;
            if (area > bestArea)
            {
                bestArea = area;
                bestEntryOffset = entryOffset;
            }
        }

        if (bestEntryOffset < 0)
        {
            return IntPtr.Zero;
        }

        var selectedLength = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(bestEntryOffset + 8, 4));
        var selectedOffset = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(bestEntryOffset + 12, 4));
        var pinned = GCHandle.Alloc(ico, GCHandleType.Pinned);
        try
        {
            return CreateIconFromResourceEx(
                IntPtr.Add(pinned.AddrOfPinnedObject(), checked((int)selectedOffset)),
                selectedLength,
                icon: true,
                version: 0x00030000,
                desiredWidth: 0,
                desiredHeight: 0,
                loadFlags: 0);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void EnsureWindowClass()
    {
        lock (WindowClassLock)
        {
            if (_windowClassRegistered)
            {
                return;
            }

            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                Instance = GetModuleHandle(null),
                WindowProcedure = SharedWindowProcedure,
                ClassName = WindowClassName
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the tray window class.");
            }

            _windowClassRegistered = true;
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == WmNcCreate)
            {
                var create = Marshal.PtrToStructure<CreateStruct>(lParam);
                _ = SetWindowLongPtr(hwnd, GwlpUserData, create.CreateParameters);
            }

            var instanceHandle = GetWindowLongPtr(hwnd, GwlpUserData);
            if (instanceHandle != IntPtr.Zero && GCHandle.FromIntPtr(instanceHandle).Target is NativeTrayWindow instance)
            {
                return instance.HandleMessage(hwnd, message, wParam, lParam);
            }
        }
        catch
        {
            // Managed exceptions cannot cross a native window-procedure boundary.
            // UI actions are dispatched by TrayIconService and report failures in
            // the same way as their normal in-app entry points.
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public IntPtr CreateParameters;
        public IntPtr Instance;
        public IntPtr Menu;
        public IntPtr Parent;
        public int Height;
        public int Width;
        public int Y;
        public int X;
        public int Style;
        public IntPtr Name;
        public IntPtr Class;
        public uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint IconId;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint UnionTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        IntPtr iconBits,
        uint iconBitsSize,
        [MarshalAs(UnmanagedType.Bool)] bool icon,
        uint version,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint itemId, string? itemText);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr trackPopupMenuParameters);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
