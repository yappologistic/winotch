# Winotch Design System

Winotch is a native WinUI 3 notch overlay. The visual system stays compact, top-attached, softly translucent, and pill-shaped rather than becoming a dashboard or landing surface. The supplied blue-gray acrylic references are the baseline for every shell mode and auxiliary surface.

## Material

- The overlay uses native `DesktopAcrylicBackdrop` through `SystemBackdropElement`. Wallpaper sampling and blur come from the Windows compositor, not a bitmap, screenshot, or simulated blur.
- Translucent color layers tune contrast over Acrylic without hiding it. A solid blue-gray fallback must remain readable when Windows disables transparency, Battery Saver is active, high contrast is enabled, or the app runs over Remote Desktop.
- Settings is a long-lived conventional window and uses `MicaBackdrop Kind="BaseAlt"` with translucent cards.
- Auxiliary flyouts use the same Desktop Acrylic material and token palette. Do not introduce an unrelated black, gray, or custom-glass treatment.

## Tokens

- Font: `Segoe UI Variable Text, Segoe UI`.
- Display font: `Segoe UI Variable Display, Segoe UI`.
- Icons: `Segoe Fluent Icons, Segoe MDL2 Assets`.
- Solid material fallback: `WinotchFallbackColor` `#E63B4C69`.
- Panel surface: `WinotchSurfaceColor` `#8A304564`.
- Raised surface: `WinotchSurfaceRaisedColor` `#B33B506F`.
- Hover surface: `WinotchSurfaceHoverColor` `#CC4B6180`.
- Pressed surface: `WinotchSurfacePressedColor` `#E0576D8C`.
- Stroke: `WinotchStrokeColor` `#47FFFFFF`.
- Primary text: `WinotchTextColor` `#FFFFFFFF`.
- Muted text: `WinotchMutedTextColor` `#C9E7ECF5`.
- Subtle text: `WinotchSubtleTextColor` `#91D8E0ED`.
- Accent: `WinotchAccentColor` `#FF60CDFF`.
- Success: `WinotchSuccessColor` `#FF6CCB5F`.
- Warning: `WinotchWarningColor` `#FFFFB84D`.
- Danger: `WinotchDangerColor` `#FFFF6B63`.

Legacy resource names such as `NotchBlack` and `NotchPanel` are compatibility aliases for these Fluent material tokens; their names do not describe the current appearance.

## Shape

- The top-attached shell keeps square top corners and a `34` DIP bottom radius so it visually joins the monitor edge.
- Mini is `260 x 68` DIPs. Live activity and compact media/toast surfaces are `440 x 76` DIPs.
- Status chips and small actions are pills.
- Repeated cards use the shared `14` DIP radius.
- Icon buttons are `32 x 32` DIPs with a `16` DIP radius; text controls use `9-10` DIP radii.
- The native window region follows the animated host size so no rectangular opaque corners remain around the Acrylic surface.

## Layout

- Expanded content uses a left system rail and a main workspace.
- The system rail owns clock/date, battery, stats, agenda/clipboard, and Settings.
- The main workspace owns Now, Controls, Activity, and bottom notification actions.
- Prefer compact rows with icons, labels, and a single primary control over tall explanatory panels.
- Keep scroll regions local to overflowing lists; do not wrap the whole expanded panel in one scroller.

## Motion

- Use WinUI/Windows Composition transform and opacity animation for state changes; keep width, height, and physical window placement synchronized through the window host.
- Mini-to-live/media motion must resolve exactly to `260 x 68` and `440 x 76`, including high-DPI monitors.
- Avoid decorative animation that competes with status changes or compact toasts.

## Accessibility and fallback

- Keep primary and secondary text legible over both live Acrylic and the solid fallback color.
- Use actual WinUI controls and their pointer, keyboard, disabled, hover, pressed, and focus states.
- Respect Windows high-contrast and reduced-transparency behavior. Material fallback is a supported state, not an error.

## Privacy

- Diagnostics export remains local and clipboard-based.
- Clipboard history, notification contents, camera frames, and shelf contents must not be added to design-time sample data, screenshots committed to Git, telemetry, or logs.
- Do not add telemetry or network calls without explicit approval.
