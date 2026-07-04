using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winotch;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly object _gate = new();
    private readonly string _path;
    private WinotchSettings _current;

    public SettingsService()
        : this(DefaultPath)
    {
    }

    public SettingsService(string path)
    {
        _path = path;
        _current = Load(path);
    }

    public event EventHandler<WinotchSettings>? Changed;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winotch",
        "settings.json");

    public WinotchSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<WinotchSettings, WinotchSettings> change)
    {
        WinotchSettings updated;
        lock (_gate)
        {
            updated = change(_current).Normalize();
            if (updated == _current)
            {
                return;
            }

            _current = updated;
            TrySave(updated);
        }

        Changed?.Invoke(this, updated);
    }

    public static string BadPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", "settings.bad.json");

    private static WinotchSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return WinotchSettings.Defaults;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            return (JsonSerializer.Deserialize<WinotchSettings>(json, JsonOptions) ?? WinotchSettings.Defaults).Normalize();
        }
        catch (JsonException)
        {
            TryRenameBadFile(path);
            return WinotchSettings.Defaults;
        }
        catch (NotSupportedException)
        {
            TryRenameBadFile(path);
            return WinotchSettings.Defaults;
        }
        catch (IOException)
        {
            return WinotchSettings.Defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return WinotchSettings.Defaults;
        }
    }

    private void TrySave(WinotchSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(directory ?? "", $"settings.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
            try
            {
                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, null);
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryRenameBadFile(string path)
    {
        try
        {
            File.Move(path, BadPathFor(path), overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter<ToastDurationScale>());
        return options;
    }
}

public sealed record WinotchSettings
{
    public static WinotchSettings Defaults => new();

    public GeneralSettings General { get; init; } = new();
    public ToastSettings Toasts { get; init; } = new();
    public CalendarSettings Calendar { get; init; } = new();

    public WinotchSettings Normalize() => this with
    {
        General = General is null ? new GeneralSettings() : General,
        Toasts = Toasts is null ? new ToastSettings() : Toasts,
        Calendar = Calendar is null ? new CalendarSettings() : Calendar.Normalize()
    };
}

public sealed record GeneralSettings
{
    public bool Use24HourClock { get; init; }
    public bool ShowDate { get; init; } = true;
    public bool StartWithWindows { get; init; }
}

public sealed record ToastSettings
{
    public bool MediaToastsEnabled { get; init; } = true;
    public bool NotificationToastsEnabled { get; init; } = true;
    public bool PriorityAlertsEnabled { get; init; } = true;
    public ToastDurationScale DurationScale { get; init; } = ToastDurationScale.Normal;
}

public sealed record CalendarSettings
{
    public bool Enabled { get; init; }
    public IReadOnlyList<string> SubscriptionUrls { get; init; } = [];

    public CalendarSettings Normalize()
    {
        var normalized = CalendarSubscriptionUrl.NormalizeAll(SubscriptionUrls);
        return SubscriptionUrls.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase)
            ? this
            : this with { SubscriptionUrls = normalized };
    }
}

public enum ToastDurationScale
{
    Short,
    Normal,
    Long
}

public static class ToastDurationScaleExtensions
{
    public static TimeSpan ApplyTo(this ToastDurationScale scale, TimeSpan duration) =>
        TimeSpan.FromMilliseconds(Math.Round(duration.TotalMilliseconds * scale.Multiplier(), MidpointRounding.AwayFromZero));

    public static double Multiplier(this ToastDurationScale scale) => scale switch
    {
        ToastDurationScale.Short => 0.75,
        ToastDurationScale.Normal => 1.0,
        ToastDurationScale.Long => 1.5,
        _ => 1.0
    };
}
