namespace Winotch;

// Shelf settings: in-memory drag-and-drop staging flyout (separate window, not an expanded-panel band).
// Owned by the Shelf & Droplets feature; expand fields here without touching SettingsService.cs.
public sealed record ShelfSettings
{
    // Enables the shelf flyout and drop target.
    public bool Enabled { get; init; } = true;
    // Maximum staged items kept in memory. Nothing is persisted to disk.
    public int Cap { get; init; } = 8;

    public ShelfSettings Normalize() => Cap > 0 ? this : this with { Cap = 8 };
}
