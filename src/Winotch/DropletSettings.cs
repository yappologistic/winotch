namespace Winotch;

// Droplets settings: tiny one-purpose flyout extensions launched from the expanded panel.
// Owned by the Shelf & Droplets feature; expand fields here without touching SettingsService.cs.
public sealed record DropletSettings
{
    // Screen-pixel color picker loupe -> copy hex/RGB.
    public bool ColorPickerEnabled { get; init; } = true;
    // Paste text -> strip formatting / change case / remove line breaks / trim / count chars.
    public bool TextScrubberEnabled { get; init; } = true;

    public DropletSettings Normalize() => this;
}
