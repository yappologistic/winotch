# Winotch Design System

Winotch is a native Windows notch overlay. The visual system should stay compact, dark, top-attached, and pill-shaped rather than becoming a dashboard or landing surface.

## Tokens

- Font: `Segoe UI Variable Text, Segoe UI`.
- Icons: `Segoe Fluent Icons, Segoe MDL2 Assets`.
- Base surface: `NotchBlack` `#050505`.
- Panel surface: `NotchPanel` `#161616`.
- Raised surface: `NotchPanelRaised` `#202020`.
- Hover surface: `NotchPanelHover` `#242424`.
- Pressed surface: `NotchPanelPressed` `#303030`.
- Stroke: `NotchStroke` `#343434`.
- Text: `NotchText` `#F6F6F4`.
- Muted text: `NotchMutedText` `#9C9C9C`.
- Accent: `NotchAccent` `#32D74B`.
- Danger: `NotchDanger` `#FF453A`.

## Shape

- The notch shell keeps a large bottom radius and no top radius.
- Status chips and small actions are pills.
- Repeated cards use `8` px corner radius.
- Tiny controls use `6-9` px radius to match existing button and list item styles.

## Layout

- Expanded content uses a left system rail and a main workspace.
- The system rail owns clock/date, battery, stats, agenda/clipboard, and Settings.
- The main workspace owns Now, Controls, Activity, and bottom notification actions.
- Prefer compact rows with icons, labels, and a single primary control over tall explanatory panels.
- Keep scroll regions local to overflowing lists; do not wrap the whole expanded panel in one scroller.

## Motion

- Use existing shell animation timing and transform/opacity reveals.
- Avoid decorative animation that competes with status changes or compact toasts.

## Privacy

- Diagnostics export remains local and clipboard-based.
- Do not add telemetry or network calls without explicit approval.
