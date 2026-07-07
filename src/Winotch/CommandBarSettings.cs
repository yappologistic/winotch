namespace Winotch;

// Command Bar settings: hotkey-driven command/input surface that morphs out of the notch.
// Owned by the Command Bar feature; expand fields here without touching SettingsService.cs.
public sealed record CommandBarSettings
{
    // Master toggle for the command bar hotkey surface.
    public bool Enabled { get; init; } = true;
    // Global hotkey parsed into RegisterHotKey modifiers + key.
    public string Hotkey { get; init; } = "Ctrl+Alt+Space";
    // Start Menu shortcut scan + ShellExecuteEx launch.
    public bool AppLauncherEnabled { get; init; } = true;
    // Enumerate top-level windows and activate the selected one.
    public bool WindowSwitcherEnabled { get; init; } = true;
    // Safe custom tokenizer/shunting-yard math evaluator (not DataTable.Compute).
    public bool CalculatorEnabled { get; init; } = true;
    // Local unit conversion only (no network; currency is off by default).
    public bool UnitConverterEnabled { get; init; } = true;
    // Map phrases to existing Winotch services (mute, wi-fi, focus, pause notch, etc.).
    public bool QuickCommandsEnabled { get; init; } = true;

    public CommandBarSettings Normalize()
    {
        var hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "Ctrl+Alt+Space" : Hotkey.Trim();
        return this with { Hotkey = hotkey };
    }
}
