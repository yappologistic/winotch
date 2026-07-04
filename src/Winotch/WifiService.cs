using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Winotch;

public sealed class WifiService
{
    public async Task<WifiStatus> GetCurrentAsync()
    {
        var output = await RunNetshAsync("wlan", "show", "interfaces");
        var current = ParseCurrentNetsh(output);
        if (current.Name is not null)
        {
            return current;
        }

        var profileOutput = await RunPowerShellAsync("""
            $profile = Get-NetConnectionProfile -InterfaceAlias 'Wi-Fi' -ErrorAction SilentlyContinue |
              Where-Object { $_.IPv4Connectivity -eq 'Internet' } |
              Select-Object -First 1 -ExpandProperty Name
            $profile
            """);
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

    public static string? ParseCurrentProfile(string output)
    {
        var profile = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return profile is null ? null : NormalizeProfileName(profile);
    }

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
        var output = await RunNetshAsync("wlan", "connect", $"name={profileName}");
        return output.Contains("completed successfully", StringComparison.OrdinalIgnoreCase)
            ? $"Connecting to {profileName}"
            : $"Windows needs a saved profile for {profileName}.";
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

    private static string NormalizeProfileName(string profile)
    {
        var match = Regex.Match(profile, @"^(?<name>.+)\s+\d+$");
        return match.Success ? match.Groups["name"].Value.Trim() : profile.Trim();
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
