using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Winotch;

public sealed class WifiService
{
    private readonly Func<string[], Task<string>> _runNetshAsync;
    private readonly Func<string, Task<string>> _runPowerShellAsync;

    public WifiService()
        : this(RunNetshAsync, RunPowerShellAsync)
    {
    }

    internal WifiService(
        Func<string[], Task<string>> runNetshAsync,
        Func<string, Task<string>> runPowerShellAsync)
    {
        _runNetshAsync = runNetshAsync;
        _runPowerShellAsync = runPowerShellAsync;
    }

    public async Task<WifiStatus> GetCurrentAsync()
    {
        var output = await _runNetshAsync(["wlan", "show", "interfaces"]);
        var current = ParseCurrentNetsh(output);
        if (current.Name is not null)
        {
            return current;
        }

        var profileOutput = await _runPowerShellAsync(WifiProfileFallback.Command);
        var profileName = ParseCurrentProfile(profileOutput);
        return new WifiStatus(profileName, null);
    }

    public async Task<IReadOnlyList<WifiNetwork>> GetNetworksAsync()
    {
        var output = await RunNetshAsync("wlan", "show", "networks", "mode=bssid");
        return ParseNetworks(output);
    }

    public static WifiStatus ParseCurrentNetsh(string output)
    {
        var ssid = ReadValue(output, "SSID");
        var signal = ReadValue(output, "Signal");
        return new WifiStatus(ssid, signal);
    }

    public static string? ParseCurrentProfile(string output) =>
        WifiProfileFallback.ParseCurrentProfile(output);

    public static IReadOnlyList<WifiNetwork> ParseNetworks(string output)
    {
        var networks = new List<WifiNetwork>();
        string? currentName = null;
        string? currentSignal = null;

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            var ssidMatch = Regex.Match(line, @"^SSID \d+ : (?<name>.+)$");
            if (ssidMatch.Success)
            {
                AddCurrent();
                currentName = ssidMatch.Groups["name"].Value.Trim();
                currentSignal = null;
                continue;
            }

            if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                currentSignal = ValueAfterColon(line);
            }
        }

        AddCurrent();
        return networks
            .Where(network => !string.IsNullOrWhiteSpace(network.Name))
            .GroupBy(network => network.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();

        void AddCurrent()
        {
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                networks.Add(new WifiNetwork(currentName, currentSignal ?? ""));
            }
        }
    }

    public async Task<string> ConnectAsync(string profileName)
    {
        var output = await _runNetshAsync(["wlan", "connect", $"name={profileName}"]);
        return output.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
            ? $"Connecting to {profileName}"
            : $"Windows needs a saved profile for {profileName}.";
    }

    public async Task<string> SetRadioEnabledAsync(bool enabled)
    {
        var verb = enabled ? "Enable-NetAdapter" : "Disable-NetAdapter";
        var output = await _runPowerShellAsync(
            "Get-NetAdapter | Where-Object { $_.Name -like '*Wi-Fi*' -or $_.InterfaceDescription -match 'Wireless|Wi-Fi|802.11' } | " +
            $"{verb} -Confirm:$false");
        return string.IsNullOrWhiteSpace(output)
            ? $"Wi-Fi {(enabled ? "enable" : "disable")} requested."
            : output.Trim();
    }

    private static string? ReadValue(string output, string name)
    {
        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                var value = ValueAfterColon(line);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static string ValueAfterColon(string line)
    {
        var colon = line.IndexOf(':');
        return colon < 0 ? "" : line[(colon + 1)..].Trim();
    }

    private static string[] SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static async Task<string> RunNetshAsync(params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "netsh";
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return await RunProcessAsync(process);
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        using var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);
        return await RunProcessAsync(process);
    }

    private static async Task<string> RunProcessAsync(Process process)
    {
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await error;
            return await output;
        }
        catch
        {
            return "";
        }
    }
}

public sealed record WifiStatus(string? Name, string? Signal)
{
    public string SignalText => string.IsNullOrWhiteSpace(Signal) ? "" : Signal;
}

public sealed record WifiNetwork(string Name, string Signal)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Signal) ? Name : $"{Name} ({Signal})";
}
