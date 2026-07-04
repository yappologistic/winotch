using System.Runtime.InteropServices;

namespace Winotch;

internal static class DdcBrightness
{
    private const string Prefix = "ddc:";

    public static IEnumerable<BrightnessDisplay> ReadDisplays()
    {
        var displays = new List<BrightnessDisplay>();
        var ordinal = 0;
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref NativeRect _, IntPtr _) =>
            {
                ordinal = AddPhysicalMonitorDisplays(monitor, ordinal, displays);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return displays;
    }

    public static void SetBrightness(string id, int value)
    {
        if (!int.TryParse(StripPrefix(id), out var targetOrdinal))
        {
            return;
        }

        var ordinal = 0;
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref NativeRect _, IntPtr _) =>
            {
                var keepGoing = true;
                WithPhysicalMonitors(monitor, physical =>
                {
                    foreach (var item in physical)
                    {
                        if (ordinal == targetOrdinal)
                        {
                            SetMonitorBrightness(item.Handle, (uint)Math.Max(0, value));
                            keepGoing = false;
                            return;
                        }

                        ordinal++;
                    }
                });

                return keepGoing;
            }, IntPtr.Zero);
        }
        catch
        {
        }
    }

    private static int AddPhysicalMonitorDisplays(IntPtr monitor, int firstOrdinal, List<BrightnessDisplay> displays)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            return firstOrdinal;
        }

        var physical = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
        {
            return firstOrdinal;
        }

        try
        {
            for (var index = 0; index < physical.Length; index++)
            {
                if (!GetMonitorBrightness(physical[index].Handle, out var minimum, out var current, out var maximum))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(physical[index].Description)
                    ? $"External display {firstOrdinal + index + 1}"
                    : physical[index].Description.Trim();
                displays.Add(new BrightnessDisplay(
                    $"{Prefix}{firstOrdinal + index}",
                    name,
                    BrightnessMath.ToPercent((int)minimum, (int)current, (int)maximum),
                    BrightnessDisplayKind.External,
                    (int)minimum,
                    (int)maximum));
            }
        }
        finally
        {
            DestroyPhysicalMonitors(count, physical);
        }

        return firstOrdinal + physical.Length;
    }

    private static void WithPhysicalMonitors(IntPtr monitor, Action<PhysicalMonitor[]> action)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            return;
        }

        var physical = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
        {
            return;
        }

        try
        {
            action(physical);
        }
        finally
        {
            DestroyPhysicalMonitors(count, physical);
        }
    }

    private static string StripPrefix(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal) ? value[Prefix.Length..] : "";

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint monitorCount);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr monitor,
        uint monitorCount,
        [Out] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint monitorCount, [In] PhysicalMonitor[] physicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(
        IntPtr monitor,
        out uint minimumBrightness,
        out uint currentBrightness,
        out uint maximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint newBrightness);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
}
