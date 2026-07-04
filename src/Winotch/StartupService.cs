using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Winotch;

public sealed class StartupService(IRunKeyStore runKeyStore)
{
    public const string RunValueName = "Winotch";

    public StartupService()
        : this(new RegistryRunKeyStore())
    {
    }

    public StartupState GetState(string executablePath)
    {
        try
        {
            var value = runKeyStore.Read(RunValueName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return new StartupState(false, true, null);
            }

            if (IsCurrentPathValue(value, executablePath))
            {
                return new StartupState(true, true, null);
            }

            runKeyStore.Write(RunValueName, FormatRunValue(executablePath));
            return new StartupState(true, true, null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            return new StartupState(false, false, ex.Message);
        }
    }

    public StartupState SetEnabled(bool enabled, string executablePath)
    {
        try
        {
            if (enabled)
            {
                runKeyStore.Write(RunValueName, FormatRunValue(executablePath));
            }
            else
            {
                runKeyStore.Delete(RunValueName);
            }

            return new StartupState(enabled, true, null);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            return new StartupState(false, false, ex.Message);
        }
    }

    public static string FormatRunValue(string executablePath) => $"\"{executablePath.Trim()}\"";

    public static bool IsCurrentPathValue(string? value, string executablePath) =>
        string.Equals(ExtractExecutablePath(value), executablePath.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string CurrentExecutablePath() =>
        Environment.ProcessPath ??
        Assembly.GetEntryAssembly()?.Location ??
        AppContext.BaseDirectory;

    public static string? ExtractExecutablePath(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }
}

public sealed record StartupState(bool IsEnabled, bool CanAccess, string? ErrorMessage);

public interface IRunKeyStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}

public sealed class RegistryRunKeyStore : IRunKeyStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new IOException("Unable to open the Windows startup registry key.");
        }

        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
