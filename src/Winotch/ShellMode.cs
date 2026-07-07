namespace Winotch;

public enum ShellMode
{
    Mini,
    FullBar,
    // Proactive compact live strip (auto-grown pill, no hover). Owned by Live Activities feature.
    Live,
    // Hotkey-driven command input surface. Owned by Command Bar feature.
    Command
}
