# Winotch

Winotch is a native WinUI 3 notch overlay. It stays centered at the top of the selected monitor and shows time, date, battery, Wi-Fi, volume, media, notifications, focus state, agenda, clipboard, controls, and system health in a compact Desktop Acrylic shell. Foreground detection currently keeps the shell in Mini for every foreground app state; hover still expands the shell when the user opens it.

## Alpha Status

Winotch is alpha, source-only software for tinkerers and testers. There is no published EXE, installer, GitHub Release, or tag yet. Clone the repository, build it locally, and expect rough edges while the app is still changing.

License: The Unlicense. Use, modify, copy, publish, or sell it with no restrictions.

## Features

- Notch shell: centered `260 x 68` Mini pill for foreground states, `440 x 76` live/media capsules, hover-expanded panel, native Desktop Acrylic, polished WinUI motion, and DPI-correct monitor centering.
- Live Activities: Mini auto-grows into a slim live strip for active calls, quick in-memory timers, playing media, or privacy activity dots, then returns to Mini when idle.
- Media: current Windows media session metadata, artwork, playback controls, and media-change toasts.
- Notifications: Windows notification list, compact notification toasts, and OS quiet-state suppression when package identity and user permission are available.
- Priority alerts: low battery, charger changes, Wi-Fi loss/reconnect, Bluetooth connects, microphone/camera activity, and focus completion toasts.
- Control center: output device switching, master volume, optional per-app mixer, microphone mute, brightness controls, and Wi-Fi profile connect.
- Focus timer: 25/5, 50/10, or custom focus sessions with pause/resume/skip/stop, auto-cycle, persistence, and compact live state.
- Clipboard history: in-memory text/link/image/file capture with privacy-format exclusions, thumbnail-only image storage, copy/delete/clear, and no disk persistence.
- Shelf: drag-and-drop staging flyout below the notch for files, text, links, and image thumbnails, with drag-out/copy/open/remove/clear.
- Droplets: local color picker and text scrubber flyouts launched from the expanded panel.
- Settings and tray: one compact fixed-size Settings window with a single close action, live settings, Start with Windows, pause/resume, feature toggles, toast gates, duration scale, JSON persistence, and a shared application/window/tray icon.
- Charging flourish: charger-connect priority toast with animated battery fill and percent readout.
- System stats: expanded-only CPU, RAM, and network text values.
- Camera mirror: live default-camera flyout below the notch with mirror toggle and no recording or saved frames.
- Calendar/agenda: ICS subscriptions, next-24-hour agenda, countdown chip, meeting reminder toast, and Join actions.
- Command bar: `Ctrl+Alt+Space` morphs the notch into a centered command row for app launch, window switching, calculator, local unit conversion, and quick Winotch actions.
- Multi-monitor: follows the foreground app monitor, cursor monitor for shell/desktop focus, and can be pinned back to the primary monitor from Settings.

## Stack

- C# and WinUI 3 on `net8.0-windows10.0.26100.0`, with Windows App SDK `2.2.0`
- Unpackaged `WinExe` alpha build targeting x64, with Windows 10 build 19041 as the declared minimum OS
- WinUI `SystemBackdropElement` with a native `DesktopAcrylicController` for the overlay, compact toasts, and auxiliary flyouts; the controller keeps the material active while another window has focus and still honors Windows transparency, contrast, and theme policy
- The same native Desktop Acrylic material for the long-lived `540 x 640` Settings window, with centered width-capped content, a fixed non-maximizable presenter, and one explicit close action
- `AppWindow`/`OverlappedPresenter` for topmost, borderless placement and narrow HWND interop for rounded regions, drag, ownership, hotkeys, and clipboard messages
- Native `GetSystemPowerStatus` for battery state
- Core Audio COM interop for master volume, per-app audio sessions, output device switching, and default microphone mute
- WMI and DDC/CI monitor APIs for brightness controls when hardware exposes them
- Windows system media transport controls for current audio metadata, artwork, and playback actions
- `netsh wlan` for Wi-Fi status, network listing, and saved-profile connect attempts
- `UserNotificationListener` for Windows toast notification access when the OS grants permission
- Windows Bluetooth and privacy status APIs for connected-device and mic/camera alerts
- Win32 clipboard format listener for the expanded-panel clipboard history
- Windows performance counters, memory status, and network interface counters for expanded-panel system stats
- `HttpClient` for user-provided ICS subscription URLs with conditional GET caching

The application UI is entirely WinUI 3 and has no WPF or Windows Forms framework dependency.

## Setup From Source

Prerequisites:

- Windows 10 version 2004 or newer, or Windows 11
- Git
- .NET 8 SDK for build, run, test, and local publish
- Windows App Runtime 2.2 for framework-dependent local runs on a machine where the WinUI development tooling has not installed it
- Visual Studio 2022 17.8 or newer with Windows application development tools is recommended for XAML editing and debugging; CLI build, run, and test use the commands below

Clone:

```powershell
git clone https://github.com/yappologistic/winotch
cd winotch
```

Build:

```powershell
dotnet build
```

Run:

```powershell
dotnet run --project src/Winotch/Winotch.csproj
```

Test:

```powershell
dotnet test
```

Hover the notch to expand it. The control center changes the current output device, tracks the master volume on the selected output, exposes active per-app audio sessions with volume/mute controls, toggles the default microphone mute, and shows brightness sliders for displays that support WMI or DDC/CI brightness. The Focus section starts 25/5, 50/10, or custom 1..180 minute focus timers, with optional auto-cycle; active timers stay visible in the compact pill and survive restart from `%LOCALAPPDATA%\Winotch\focus-timer.json`. Media buttons control the focused Windows media session in the expanded capsule and in the brief media toast. Notification toasts show app/sender text and time when supported notification access is available. Priority status toasts appear for focus completions, low battery, charger connect/disconnect, Wi-Fi loss/reconnect, Bluetooth device connect, and mic/camera activity. The expanded panel also shows a small clipboard history with text, links, image thumbnails, and copied file lists. Wi-Fi connect works for saved Windows Wi-Fi profiles.

On multi-monitor setups, the notch follows the monitor containing the foreground app while the shell mode remains Mini until hover expansion or a compact toast. When the desktop or shell has focus, it follows the monitor containing the cursor, falling back to the last monitor and then the primary monitor if needed. Settings can disable active-monitor following and keep the notch on the primary monitor.

The Calendar settings group accepts one or more `webcal://`, `https://`, or `http://` ICS URLs, refreshes them every five minutes, and adds the next three 24-hour agenda items plus Join buttons for Zoom, Teams, and Google Meet links.

The Live Activities settings group controls four local-only signals: camera/microphone/screen-share dots, a now-playing strip, an in-memory quick timer, and optional call detection. Call detection is off by default and uses local process/window-title heuristics for Teams, Zoom, and Google Meet; it does not send titles anywhere. The quick timer is separate from the persisted focus timer and clears when Winotch exits.

## Command Bar

Press `Ctrl+Alt+Space` to morph the notch into a command input row and local results list. `Esc` collapses it; Up/Down move selection; Enter runs the selected result. Settings can change the hotkey and enable or disable the app launcher, window switcher, calculator, unit converter, and quick-command providers.

The command bar is local-only. It scans Start Menu shortcuts, enumerates visible top-level windows, evaluates math with a custom safe parser, converts local units, and runs existing Winotch actions such as mute, Wi-Fi adapter requests, focus timer start/stop, and pause/resume. Currency conversion and web lookup are intentionally not included.

The tray icon opens Settings, pauses/resumes the overlay, toggles Start with Windows, and exits the app. Winotch allows one running process at a time, and repeated Settings requests activate the existing Settings window instead of creating duplicates. Settings changes apply live and persist as indented JSON at `%LOCALAPPDATA%\Winotch\settings.json`; corrupt JSON is moved aside as `settings.bad.json` and defaults are used. Settings can disable clipboard capture, per-app mixer, stats sampling, and active-monitor following without restarting.

Charger-connect priority toasts add a compact green battery-fill flourish with a prominent percent readout. Charger disconnect keeps the existing quieter status toast.

The expanded System column shows compact CPU, RAM, and network text values. Sampling starts only while the expanded panel is visible and stops again on collapse.

The camera button in the expanded control center opens a small live mirror flyout below the notch. The preview is mirrored by default, has a one-click normal-view toggle, and closes on X, Esc, outside click, notch collapse, pause, or power transition. Winotch never records or saves camera frames; the camera device is opened only for the live preview and released on close. The mirror uses the default Windows camera only; a camera picker is intentionally out of scope.

The shelf button opens a separate topmost flyout below the notch. Dropping files, text, links, or images onto either the notch or the Shelf stages compact rows in memory; rows can be dragged back out, copied, opened, removed, or cleared. The Shelf remains open across focus, notch-mode, pause, and power-state changes, and closes only through its X, Escape, the Shelf toggle, disabling the feature, or app exit. It is capped by Settings, defaults to 8 items, uses the same clipboard privacy exclusion formats as clipboard history, stores only image thumbnails, and clears when Winotch exits.

Droplets are small local flyouts from the expanded panel. Color picker samples a clicked screen pixel with `CopyFromScreen` and copies hex/RGB text. Text scrubber runs pure string operations for trimming, line-break removal, case changes, and character counts.

## Test Coverage

Run the full regression suite before sharing a build:

```powershell
dotnet test
```

The tests cover Wi-Fi parsing, battery fill/color thresholds, focus timer state transitions/persistence/formatting, ICS parsing/recurrence/timezone/join-link/countdown behavior, media toast geometry/timing and dedupe behavior, notification toast metadata/actions/dedupe behavior, clipboard history preview/privacy/dedupe behavior, Shelf storage and native Windows drop payloads, priority status alert transitions, control-center naming/device/brightness/debounce state logic, system stats sampling/rate math/formatting, camera mirror lifecycle/layout/suppression behavior, command-bar scoring/calculator/unit/hotkey logic, settings persistence/startup/single-window/icon helpers, shell mode sizing heuristics, active-monitor selection, app-bar DPI conversion, refresh-rate normalization, and animation timing guards.

Charging flourish tests cover reusable fill-width math, animation parameter derivation, charger-alert mapping, full and low-percent edge cases, and existing low-battery queue ordering.

## Clipboard History

Clipboard history stays in memory while the app is running, capped to the latest 10 items. Winotch does not write clipboard history to disk, settings, logs, or roaming storage. That is the privacy default: closing Winotch clears its private clipboard history.

The listener skips clipboard updates marked with Windows privacy exclusion formats such as `ExcludeClipboardContentFromMonitorProcessing` or `CanIncludeInClipboardHistory = 0`. Image captures store only a small thumbnail, not the original full bitmap.

Diagnostics export copies a local device and settings summary to the Windows clipboard from Settings. It includes OS/runtime, monitor geometry/DPI, battery, notification access, startup state, and feature toggles, but omits clipboard contents, notification text, calendar URLs, Wi-Fi names, camera frames, audio device names, and raw user-profile paths.

## Camera Mirror

The camera mirror uses `Windows.Media.Capture.MediaCapture` with CPU-backed frame reading and renders frames into a WinUI `WriteableBitmap` as an in-memory preview. If Windows reports no camera, access denial, or exclusive-use failure, the flyout shows a quiet inline message instead of retrying. Opening the mirror suppresses Winotch's own camera-in-use priority alert while the preview is active.

## Shelf

Shelf state is memory-only and capped by Settings. Winotch does not write staged shelf items to disk, settings, logs, or roaming storage. Closing Winotch clears the shelf. File rows keep file paths, text/link rows keep capped text, and image rows keep only a small thumbnail.

## Droplets

Color picker and text scrubber are fully local utilities. They add no packages, telemetry, or network calls.

## Optional Local Publish

Winotch is currently an unpackaged desktop app with no published GitHub binary. If you want a local EXE from source, publish into a local folder.

Framework-dependent publish, smallest output. Install the matching Windows App Runtime 2.2 on the target machine:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -o "$env:LOCALAPPDATA\Winotch"
```

Self-contained publish, carrying .NET and Windows App SDK runtime files in the output folder:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o "$env:LOCALAPPDATA\Winotch"
```

The self-contained output is a folder deployment, not a release binary. Do not commit it, attach it to GitHub, or treat it as a supported installer while Winotch remains source-only alpha software.

Run the local publish:

```powershell
& "$env:LOCALAPPDATA\Winotch\Winotch.exe"
```

Start with Windows can be toggled from Settings or the tray menu. Winotch writes the HKCU Run value `Winotch` to the quoted current executable path and repairs stale paths when it reads the setting.

Uninstall the local publish:

```powershell
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Winotch" -ErrorAction SilentlyContinue
Remove-Item "$env:LOCALAPPDATA\Winotch" -Recurse -Force
```

## Notification Access

Windows requires explicit user permission for notification listener access. Full all-app history through `UserNotificationListener` also requires the User Notification Listener capability in a package manifest and therefore package identity; a plain unpackaged source run cannot assume that capability. Current source builds treat notification history as optional and do not inspect unrelated windows as a substitute. A future package-with-external-location identity can add the capability without moving Winotch's binaries into MSIX, but that is not part of the current source-only alpha deployment. Winotch requests notification history access only when the user clicks Request access in Settings; passive status refreshes do not open the Windows permission prompt.

Winotch respects the Windows notification state before showing its own compact notification toast. If Windows reports that notifications should be suppressed, including Do Not Disturb/quiet states, Winotch updates the notification list but does not pop a toast.

## Platform Notes

- Camera: Winotch opens the default Windows camera only while the live mirror flyout is visible. It does not record, persist frames, or offer a camera picker.
- Clipboard: clipboard history is in-memory only and clears when Winotch exits.
- Live Activities: quick live timers and activity arbitration are in-memory only. Call detection, when enabled, checks local process names and window titles without telemetry or network calls.
- Shelf: staged shelf items are in-memory only and clear when Winotch exits.
- Droplets: color picking and text scrubbing are local-only; no network calls are added.
- Notifications: full notification history depends on Windows permission and packaged-app capabilities; the source-run alpha degrades quietly to live-toast watching when those are unavailable.
- Calendar: Winotch fetches only user-provided ICS URLs and caches conditional GET metadata locally.
- Settings: local JSON state lives under `%LOCALAPPDATA%\Winotch`; Winotch does not add telemetry or background network calls beyond user-provided calendar URLs.
