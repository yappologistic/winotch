using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winotch;

public static class SystemStatus
{
    private const byte UnknownBatteryPercent = byte.MaxValue;
    private const byte BatteryFlagCharging = 0x08;

    public static BatteryInfo GetBattery()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // BatteryLifePercent is 255 when Windows cannot report a value. The
        // existing UI contract has no unknown state, so keep its safe full-scale
        // fallback while using the native percentage whenever one is available.
        var percent = status.BatteryLifePercent == UnknownBatteryPercent
            ? 100
            : Math.Clamp(status.BatteryLifePercent, (byte)0, (byte)100);
        var charging = (status.BatteryFlag & BatteryFlagCharging) != 0;
        return new BatteryInfo(percent, charging);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}

public sealed record BatteryInfo(int Percent, bool IsCharging);
