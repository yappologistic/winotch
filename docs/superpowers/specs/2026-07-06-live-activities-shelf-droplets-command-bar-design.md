# Winotch Feature Design — Live Activities, Shelf & Droplets, Command Bar

**Date:** 2026-07-06
**Status:** Approved (build from branch `t3code/feature-ideas-exploration`, not `main`)
**Inspiration:** Alcove (Dynamic Island / Live Activities) and Droppy (Shelf / Droplets)

This spec defines three new feature directions for Winotch. All three reuse the
existing design language (`DESIGN.md` tokens, Segoe UI Variable, pill shell,
`ShellAnimationTiming` motion) and the established architecture patterns:

- Pure-logic services + models in their own files (testable without WPF).
- Feature UI in **its own XAML UserControl or flyout window** (the
  `CameraMirrorWindow` / `ClipboardHistoryPanel` pattern), not a band baked
  into the expanded panel.
- Settings as additive JSON groups, each in its own record file.
- Deterministic xunit tests, one file per feature.

## A — Live Activities

### Goal
A new shell mode `Live` between `Mini` and `Expanded`. The compact pill
**auto-grows** a slim live strip for the single most important ongoing activity
— no hover required — then reverts to `Mini` when the activity ends. It does
NOT open the full expanded panel (respects "pill-shaped, not a dashboard").

### v1 activities (priority order, one visible at a time)
1. **Activity dots** — tiny colored dots on the pill edge: orange = camera,
   red = mic, purple = screen-share. Reuses the Windows privacy-usage APIs
   already powering `PriorityStatusTracker`.
2. **Now-Playing strip** — slim artwork + title + scrubber extending the pill
   while media plays; reverts on pause/stop. Reuses `MediaService`.
3. **Transient countdown timer** — a quick "5 min" timer with a progress ring,
   separate from the focus timer (lightweight, in-memory, no persistence).
4. **Active-call chip** *(stretch, off by default)* — detect Teams/Zoom/Meet
   in-call via process + window-title heuristics → "Call · 12:04". Gated by
   setting because heuristics can be flaky.

### Integration points
- `LiveActivityService` + `LiveActivity` models (pure logic, testable priority
  arbitration) — NEW files.
- New `Live` value in `ShellMode` (added in the prep commit).
- Sizing for the live strip in `ShellMetrics` (NEW geometry helper, e.g.
  `LiveStrip(...)`); the agent owns the exact dimensions.
- `Mini ↔ Live` transition via `ShellAnimator` (reuse existing timing).
- `LiveActivitySettings` record (pre-created in the prep commit; agent expands
  fields + `Normalize`).
- Settings UI in `SettingsWindow` (agent adds a Live Activities group).
- Tests: `LiveActivityTests.cs` — priority arbitration, mode selection, timer
  math, call-detection heuristics, dot mapping.

### Key decision
Auto-show a slim live pill (recommended — this is the whole value prop,
mirroring Alcove), kept small and auto-dismissing. Never auto-open the full
expanded panel.

## B — Shelf & Droplets

### Goal — Shelf (redesigned)
The repo previously had a **file shelf** baked into the expanded panel as a
horizontal band of tiles. It was removed ("Remove expanded shelf band",
"Remove remaining file shelf surfaces") because it was **cramped** and competed
with the compact notch design. The new shelf avoids that failure by being a
**separate flyout window** below the notch (the `CameraMirrorWindow` pattern),
NOT a band inside the expanded panel.

Drag files/text/links/images onto the notch (or a shelf button in the expanded
panel) → a compact flyout of slim staged rows → drag out, copy, open, or clear.
**In-memory only, privacy-first** (reuses `ClipboardPrivacyPolicy` patterns),
cap ~8 items. No disk persistence.

### Goal — Droplets v1 (three self-contained flyouts from the expanded panel)
1. **Color picker** — screen-pixel loupe → copy hex/RGB to clipboard
   (`CopyFromScreen`, no package).
2. **Text scrubber** — paste text → strip formatting / change case / remove
   line breaks / trim whitespace / count chars (pure string ops).

### Integration points
- `ShelfService` + `ShelfModels` (in-memory, privacy) + `ShelfFlyout` window —
  NEW files mirroring the camera-mirror flyout lifecycle.
- `Droplets/` area: `ColorPickerDroplet` and `TextScrubberDroplet` — each a small service + flyout (NEW files).
- `ShelfSettings` + `DropletSettings` records (pre-created in the prep commit;
  agent expands fields + `Normalize`).
- Settings UI groups for Shelf and Droplets.
- Tests: `ShelfTests.cs` (cap/dedupe/privacy/clear), `DropletTests.cs` (color
  math and text transforms).

### Key decisions
- Shelf as a flyout window (avoids the cramped-band removal).
- Shelf is in-memory only and clears on exit (matches clipboard history privacy
  posture). Document this plainly per the privacy rules.

## C — Command Bar

### Goal
A global hotkey (default `Ctrl+Alt+Space`, configurable) morphs the notch into
a **command input row + results list** using a new `Command` shell mode (reuses
centered `ShellMetrics` geometry → feels native, not a separate floating window
like PowerToys Run). `Esc` collapses.

### v1 providers (ranked, fuzzy match)
1. **App launcher** — scan Start Menu known-folder shortcuts + launch via
   `ShellExecuteEx`.
2. **Running window switcher** — enumerate top-level windows (reuses
   `ForegroundWindowService` patterns) + activate the selected window.
3. **Inline calculator** — a **safe custom tokenizer / shunting-yard
   evaluator** (NOT `DataTable.Compute` — security risk).
4. **Unit conversion** — local units only (no network, honors privacy rules).
   Currency is off by default (would need a network feed).
5. **Quick commands** — map to *existing* Winotch services: "toggle night
   light", "mute", "wi-fi on/off", "focus start 25", "pause notch".

### Integration points
- `CommandBar/` area: `CommandBarService` + `ICommandProvider` + 5 providers
  (`AppLaunchProvider`, `WindowSwitchProvider`, `CalculatorProvider`,
  `UnitConverterProvider`, `QuickCommandProvider`) — NEW files.
- New `Command` value in `ShellMode` (added in the prep commit).
- `Command` shell geometry in `ShellMetrics` (agent owns dimensions).
- Global hotkey via `RegisterHotKey` (Win32) on the notch HWND.
- `CommandBarSettings` record (pre-created in the prep commit; agent expands
  fields + `Normalize`).
- Settings UI group for Command Bar (hotkey + provider toggles).
- Tests: `CommandBarTests.cs` — fuzzy scoring, calculator (security + math),
  unit conversion, provider ranking, hotkey parsing.

### Key decisions
- New `Command` shell mode reusing centered notch geometry (on-design).
- Safe custom calculator evaluator (not `DataTable.Compute`).
- Currency conversion off by default (privacy / no-network).

## Orchestration plan

The three features are largely independent but share a few files. To keep
parallel agents from clobbering each other:

1. **Prep commit on this branch** (done before dispatch): add the `Live` and
   `Command` `ShellMode` values; pre-create four settings record files
   (`LiveActivitySettings`, `ShelfSettings`, `DropletSettings`,
   `CommandBarSettings`) each with a `Normalize()` returning `this`; wire them
   into `WinotchSettings` + `Normalize`. This compiles and tests stay green.
2. **Three worktrees** branched from this branch: `feature/live-activities`,
   `feature/shelf-droplets`, `feature/command-bar`.
3. **Each agent's rule:** add NEW files for your feature (services, models,
   flyout/UserControl XAML, tests); only edit YOUR own pre-created settings
   record file (do NOT edit `SettingsService.cs`); write code comments; update
   YOUR doc section; self-review at least twice; `dotnet build` and
   `dotnet test` must pass before you finish.
4. Integrate A → B → C sequentially, resolving the small additive conflicts,
   then run the full suite + `dotnet format`.

## Privacy (per `AGENTS.md`)
- Shelf: in-memory only, clears on exit. Document plainly.
- Droplets: color picker and text scrubber are fully local with no network.
- Command Bar: app/window/calculator/units/quick-commands are all local. No
  web search or currency fetch without explicit approval.
- No telemetry or background network calls are added by any of these features.
