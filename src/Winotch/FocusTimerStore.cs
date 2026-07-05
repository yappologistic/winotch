using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winotch;

public sealed class FocusTimerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public FocusTimerStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winotch",
            "focus-timer.json"))
    {
    }

    public FocusTimerStore(string path)
    {
        _path = path;
    }

    public FocusTimerAdvance Load(DateTimeOffset now)
    {
        var state = ReadState();
        var advanced = state.AdvanceTo(now);
        Save(advanced.State);
        return advanced;
    }

    public void Save(FocusTimerState state)
    {
        if (!state.IsActive)
        {
            Clear();
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(FocusTimerFile.FromState(state), JsonOptions));
    }

    public void Clear()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private FocusTimerState ReadState()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return FocusTimerState.Stopped;
            }

            var file = JsonSerializer.Deserialize<FocusTimerFile>(File.ReadAllText(_path), JsonOptions);
            return file?.ToState() ?? FocusTimerState.Stopped;
        }
        catch
        {
            return FocusTimerState.Stopped;
        }
    }

    private sealed record FocusTimerFile(
        FocusTimerStatus Status,
        FocusTimerPhase Phase,
        long FocusDurationTicks,
        long BreakDurationTicks,
        bool AutoCycle,
        DateTimeOffset PhaseStartedAtUtc,
        long PausedElapsedTicks,
        DateTimeOffset? PausedAtUtc,
        int CompletedFocusCycles)
    {
        public static FocusTimerFile FromState(FocusTimerState state) => new(
            state.Status,
            state.Phase,
            state.FocusDuration.Ticks,
            state.BreakDuration.Ticks,
            state.AutoCycle,
            state.PhaseStartedAtUtc.ToUniversalTime(),
            state.PausedElapsed.Ticks,
            state.PausedAtUtc?.ToUniversalTime(),
            state.CompletedFocusCycles);

        public FocusTimerState ToState()
        {
            var state = new FocusTimerState(
                Status,
                Phase,
                TimeSpan.FromTicks(FocusDurationTicks),
                TimeSpan.FromTicks(BreakDurationTicks),
                AutoCycle,
                PhaseStartedAtUtc.ToUniversalTime(),
                TimeSpan.FromTicks(Math.Max(0, PausedElapsedTicks)),
                PausedAtUtc?.ToUniversalTime(),
                Math.Max(0, CompletedFocusCycles));
            return IsValid(state) ? state : FocusTimerState.Stopped;
        }

        private static bool IsValid(FocusTimerState state) =>
            Enum.IsDefined(state.Status) &&
            Enum.IsDefined(state.Phase) &&
            state.IsActive &&
            new FocusTimerSettings(state.FocusDuration, state.BreakDuration, state.AutoCycle).IsValid &&
            (state.Status != FocusTimerStatus.Paused || state.PausedAtUtc is not null);
    }
}
