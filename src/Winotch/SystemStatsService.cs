using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Winotch;

public sealed class SystemStatsService : IDisposable
{
    private const int SampleCapacity = 60;
    private readonly FixedSampleBuffer _cpuSamples = new(SampleCapacity);
    private readonly FixedSampleBuffer _ramSamples = new(SampleCapacity);
    private readonly FixedSampleBuffer _networkSamples = new(SampleCapacity);
    private readonly NetworkThroughputSampler _network = new();
    private CpuUsageCounter? _cpu;
    private bool _cpuUnavailable;
    private bool _memoryUnavailable;
    private bool _networkUnavailable;
    private bool _running;

    public void BeginSession()
    {
        EndSession();
        _cpuSamples.Clear();
        _ramSamples.Clear();
        _networkSamples.Clear();
        _network.Reset();

        // Perf counters are expensive and can be corrupt; create them only while the panel is expanded.
        _cpu = CpuUsageCounter.TryCreate();
        _cpuUnavailable = _cpu is null;
        _memoryUnavailable = false;
        _networkUnavailable = false;
        _running = true;
    }

    public void EndSession()
    {
        _running = false;
        _cpu?.Dispose();
        _cpu = null;
        _network.Reset();
    }

    public SystemStatsSnapshot Read()
    {
        if (!_running)
        {
            return new SystemStatsSnapshot(null, null, null);
        }

        return new SystemStatsSnapshot(ReadCpu(), ReadRam(), ReadNetwork());
    }

    public void Dispose() => EndSession();

    private SystemStatRowSnapshot? ReadCpu()
    {
        if (_cpuUnavailable || _cpu is null)
        {
            return null;
        }

        var percent = _cpu.ReadPercent();
        if (percent is null)
        {
            _cpuUnavailable = true;
            _cpu.Dispose();
            _cpu = null;
            _cpuSamples.Clear();
            return null;
        }

        _cpuSamples.Push(percent.Value);
        return new SystemStatRowSnapshot(SystemStatsFormatter.FormatCpu(percent.Value), _cpuSamples.Snapshot());
    }

    private SystemStatRowSnapshot? ReadRam()
    {
        if (_memoryUnavailable)
        {
            return null;
        }

        if (!MemoryStatusReader.TryRead(out var memory))
        {
            _memoryUnavailable = true;
            _ramSamples.Clear();
            return null;
        }

        var usedPercent = memory.TotalBytes == 0
            ? 0
            : (double)memory.UsedBytes / memory.TotalBytes * 100;
        _ramSamples.Push(usedPercent);
        return new SystemStatRowSnapshot(
            SystemStatsFormatter.FormatRam(memory.UsedBytes, memory.TotalBytes),
            _ramSamples.Snapshot());
    }

    private SystemStatRowSnapshot? ReadNetwork()
    {
        if (_networkUnavailable)
        {
            return null;
        }

        try
        {
            var rates = _network.Sample(DateTimeOffset.UtcNow);
            _networkSamples.Push(rates.DownBytesPerSecond + rates.UpBytesPerSecond);
            return new SystemStatRowSnapshot(SystemStatsFormatter.FormatNetwork(rates), _networkSamples.Snapshot());
        }
        catch
        {
            _networkUnavailable = true;
            _networkSamples.Clear();
            return null;
        }
    }
}

internal sealed class CpuUsageCounter : IDisposable
{
    private static readonly (string Category, string Counter, string Instance)[] CounterPreferences =
    [
        ("Processor Information", "% Processor Utility", "_Total"),
        ("Processor", "% Processor Time", "_Total")
    ];

    private readonly PerformanceCounter _counter;

    private CpuUsageCounter(PerformanceCounter counter)
    {
        _counter = counter;
    }

    public static CpuUsageCounter? TryCreate()
    {
        foreach (var preference in CounterPreferences)
        {
            PerformanceCounter? counter = null;
            try
            {
                counter = new PerformanceCounter(
                    preference.Category,
                    preference.Counter,
                    preference.Instance,
                    readOnly: true);
                _ = counter.NextValue();
                return new CpuUsageCounter(counter);
            }
            catch
            {
                counter?.Dispose();
            }
        }

        return null;
    }

    public double? ReadPercent()
    {
        try
        {
            return Math.Clamp(_counter.NextValue(), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _counter.Dispose();
}

internal sealed record MemoryStats(ulong UsedBytes, ulong TotalBytes);

internal static class MemoryStatusReader
{
    public static bool TryRead(out MemoryStats stats)
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            stats = new MemoryStats(0, 0);
            return false;
        }

        stats = new MemoryStats(status.TotalPhys - status.AvailPhys, status.TotalPhys);
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

internal sealed class NetworkThroughputSampler
{
    private Dictionary<string, NetworkCounterSnapshot> _previous = [];
    private DateTimeOffset? _previousAt;

    public NetworkRates Sample(DateTimeOffset now)
    {
        var current = NetworkCounterReader.ReadActivePhysicalAdapters();
        var rates = _previousAt is null
            ? new NetworkRates(0, 0)
            : NetworkRateCalculator.FromSnapshots(_previous.Values, current, now - _previousAt.Value);

        // First sample and newly appeared NICs establish a baseline instead of inventing traffic.
        _previous = current.ToDictionary(snapshot => snapshot.Id, StringComparer.Ordinal);
        _previousAt = now;
        return rates;
    }

    public void Reset()
    {
        _previous.Clear();
        _previousAt = null;
    }
}

internal static class NetworkCounterReader
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "bluetooth",
        "docker",
        "hyper-v",
        "loopback",
        "npcap",
        "pseudo",
        "tap",
        "tunnel",
        "virtual",
        "virtualbox",
        "vmware",
        "vpn",
        "wsl"
    ];

    public static IReadOnlyList<NetworkCounterSnapshot> ReadActivePhysicalAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsActivePhysicalAdapter)
            .Select(TryRead)
            .OfType<NetworkCounterSnapshot>()
            .ToArray();

    private static NetworkCounterSnapshot? TryRead(NetworkInterface adapter)
    {
        try
        {
            var stats = adapter.GetIPStatistics();
            return new NetworkCounterSnapshot(adapter.Id, stats.BytesReceived, stats.BytesSent);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsActivePhysicalAdapter(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up || adapter.Speed <= 0)
        {
            return false;
        }

        if (adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.Wwanpp
            or NetworkInterfaceType.Wwanpp2))
        {
            return false;
        }

        var name = $"{adapter.Name} {adapter.Description}";
        return !VirtualAdapterMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
