using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Winotch;

public sealed class PriorityStatusService
{
    public PriorityStatusSnapshot Read(BatteryInfo battery, WifiStatus wifi) =>
        new(
            battery,
            wifi,
            ReadConnectedBluetoothDevice(),
            IsMicrophoneActive(),
            IsCameraActive());

    public static bool IsActivePrivacyUse(long? lastUsedStart, long? lastUsedStop) =>
        lastUsedStart.GetValueOrDefault() > 0 && lastUsedStop == 0;

    public static bool IsMicrophoneActive() => IsCapabilityActive("microphone");

    private static bool IsCameraActive() => IsCapabilityActive("webcam");

    private static bool IsCapabilityActive(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}");
            return HasActiveCapabilityUse(key);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasActiveCapabilityUse(RegistryKey? key)
    {
        if (key is null)
        {
            return false;
        }

        if (IsActivePrivacyUse(ReadInt64(key.GetValue("LastUsedTimeStart")), ReadInt64(key.GetValue("LastUsedTimeStop"))))
        {
            return true;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName);
            if (HasActiveCapabilityUse(child))
            {
                return true;
            }
        }

        return false;
    }

    private static long? ReadInt64(object? value) => value switch
    {
        long number => number,
        int number => number,
        string text when long.TryParse(text, out var number) => number,
        _ => null
    };

    private static string? ReadConnectedBluetoothDevice()
    {
        try
        {
            var search = new BluetoothDeviceSearchParams
            {
                dwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
                fReturnAuthenticated = true,
                fReturnRemembered = true,
                fReturnConnected = true
            };
            var device = NewBluetoothDeviceInfo();
            var handle = BluetoothFindFirstDevice(ref search, ref device);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                while (true)
                {
                    if (device.fConnected && !string.IsNullOrWhiteSpace(device.szName))
                    {
                        return device.szName.Trim();
                    }

                    device = NewBluetoothDeviceInfo();
                    if (!BluetoothFindNextDevice(handle, ref device))
                    {
                        return null;
                    }
                }
            }
            finally
            {
                BluetoothFindDeviceClose(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static BluetoothDeviceInfo NewBluetoothDeviceInfo() =>
        new() { dwSize = Marshal.SizeOf<BluetoothDeviceInfo>() };

    [DllImport("Bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams searchParams,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("Bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(
        IntPtr findHandle,
        ref BluetoothDeviceInfo deviceInfo);

    [DllImport("Bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SystemTime stLastSeen;
        public SystemTime stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }
}

public sealed class PriorityStatusTracker
{
    private const int LowBatteryPercent = 20;
    private readonly Queue<PriorityStatusAlert> _pending = new();
    private PriorityStatusSnapshot? _last;

    public PriorityStatusAlert? Next(PriorityStatusSnapshot current)
    {
        if (_last != current)
        {
            EnqueueChanges(current, _last);
            _last = current;
        }

        return _pending.Count == 0 ? null : _pending.Dequeue();
    }

    private void EnqueueChanges(PriorityStatusSnapshot current, PriorityStatusSnapshot? last)
    {
        if (last is null)
        {
            EnqueueInitialAlerts(current);
            return;
        }

        if (!last.CameraActive && current.CameraActive)
        {
            _pending.Enqueue(PriorityStatusAlert.CameraActive());
        }

        if (!last.MicrophoneActive && current.MicrophoneActive)
        {
            _pending.Enqueue(PriorityStatusAlert.MicrophoneActive());
        }

        if (IsLowBattery(current.Battery) && !IsLowBattery(last.Battery))
        {
            _pending.Enqueue(PriorityStatusAlert.LowBattery(current.Battery.Percent));
        }

        if (!last.Battery.IsCharging && current.Battery.IsCharging)
        {
            _pending.Enqueue(PriorityStatusAlert.ChargerConnected(current.Battery.Percent));
        }
        else if (last.Battery.IsCharging && !current.Battery.IsCharging)
        {
            _pending.Enqueue(PriorityStatusAlert.ChargerDisconnected(current.Battery.Percent));
        }

        EnqueueWifiChange(current.Wifi.Name, last.Wifi.Name);

        if (!string.IsNullOrWhiteSpace(current.BluetoothDeviceName) &&
            !string.Equals(current.BluetoothDeviceName, last.BluetoothDeviceName, StringComparison.Ordinal))
        {
            _pending.Enqueue(PriorityStatusAlert.BluetoothConnected(current.BluetoothDeviceName));
        }
    }

    private void EnqueueInitialAlerts(PriorityStatusSnapshot current)
    {
        if (current.CameraActive)
        {
            _pending.Enqueue(PriorityStatusAlert.CameraActive());
        }

        if (current.MicrophoneActive)
        {
            _pending.Enqueue(PriorityStatusAlert.MicrophoneActive());
        }

        if (IsLowBattery(current.Battery))
        {
            _pending.Enqueue(PriorityStatusAlert.LowBattery(current.Battery.Percent));
        }
    }

    private void EnqueueWifiChange(string? currentName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(currentName))
        {
            _pending.Enqueue(PriorityStatusAlert.WifiLost(lastName));
        }
        else if (!string.IsNullOrWhiteSpace(currentName) &&
            !string.Equals(currentName, lastName, StringComparison.OrdinalIgnoreCase))
        {
            _pending.Enqueue(PriorityStatusAlert.WifiReconnected(currentName));
        }
    }

    private static bool IsLowBattery(BatteryInfo battery) =>
        !battery.IsCharging && battery.Percent <= LowBatteryPercent;
}

public sealed record PriorityStatusSnapshot(
    BatteryInfo Battery,
    WifiStatus Wifi,
    string? BluetoothDeviceName,
    bool MicrophoneActive,
    bool CameraActive);

public sealed record PriorityStatusAlert(
    string Icon,
    string Title,
    string Body,
    int? BatteryPercent = null,
    bool ShowsChargingFlourish = false)
{
    public static PriorityStatusAlert LowBattery(int percent) =>
        new("\uE850", "Low battery", $"{percent}% remaining");

    public static PriorityStatusAlert ChargerConnected(int percent) =>
        new("\uE83E", "Charger connected", $"{percent}% and charging", percent, ShowsChargingFlourish: true);

    public static PriorityStatusAlert ChargerDisconnected(int percent) =>
        new("\uE850", "Charger disconnected", $"{percent}% battery");

    public static PriorityStatusAlert WifiLost(string name) =>
        new("\uE701", "Wi-Fi disconnected", $"{name} connection lost");

    public static PriorityStatusAlert WifiReconnected(string name) =>
        new("\uE701", "Wi-Fi reconnected", $"Connected to {name}");

    public static PriorityStatusAlert BluetoothConnected(string name) =>
        new("\uE702", "Bluetooth connected", name);

    public static PriorityStatusAlert MicrophoneActive() =>
        new("\uE720", "Microphone active", "An app is using your microphone");

    public static PriorityStatusAlert CameraActive() =>
        new("\uE722", "Camera active", "An app is using your camera");
}
