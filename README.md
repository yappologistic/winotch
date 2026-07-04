# Winotch

Winotch is a native Windows notch overlay. It stays centered at the top of the active monitor and shows time, date, battery, Wi-Fi, volume, media, notifications, focus state, agenda, clipboard, controls, and system health in a compact black shell that expands on hover.

## Alpha Status

Winotch is alpha, source-only software for tinkerers and testers. There is no published EXE, installer, GitHub Release, or tag yet. Clone the repository, build it locally, and expect rough edges while the app is still changing.

License: The Unlicense. Use, modify, copy, publish, or sell it with no restrictions.

## Features

- Notch shell: mini pill, fullscreen-aware full bar, expanded panel, compact toasts, polished WPF motion, and DPI-correct monitor centering.
- Media: current Windows media session metadata, artwork, playback controls, and media-change toasts.
- Notifications: Windows notification list, compact notification toasts, live action buttons when Windows exposes them, and OS quiet-state suppression.
- Priority alerts: low battery, charger changes, Wi-Fi loss/reconnect, Bluetooth connects, microphone/camera activity, and focus completion toasts.
- Control center: output device switching, master volume, optional per-app mixer, microphone mute, brightness controls, and Wi-Fi profile connect.
- Focus timer: 25/5, 50/10, or custom focus sessions with pause/resume/skip/stop, auto-cycle, persistence, and compact live state.
- Clipboard history: in-memory text/link/image/file capture with privacy-format exclusions, thumbnail-only image storage, copy/delete/clear, and no disk persistence.
- Settings and tray: live settings, Start with Windows, pause/resume, feature toggles, toast gates, duration scale, and JSON persistence.
- Charging flourish: charger-connect priority toast with animated battery fill and percent readout.
- System stats: expanded-only CPU, RAM, and network text values.
- Camera mirror: live default-camera flyout below the notch with mirror toggle and no recording or saved frames.
- Calendar/agenda: ICS subscriptions, next-24-hour agenda, countdown chip, meeting reminder toast, and Join actions.
- Multi-monitor: follows the foreground app monitor, cursor monitor for shell/desktop focus, and can be pinned back to the primary monitor from Settings.

## Stack

- C# WPF on `net8.0-windows10.0.19041.0`
- Transparent, topmost desktop window for the notch shell
- Windows Forms power status for battery
- Core Audio COM interop for master volume, per-app audio sessions, output device switching, and default microphone mute
- WMI and DDC/CI monitor APIs for brightness controls when hardware exposes them
- Windows system media transport controls for current audio metadata, artwork, and playback actions
- `netsh wlan` for Wi-Fi status, network listing, and saved-profile connect attempts
- `UserNotificationListener` for Windows toast notification access when the OS grants permission
- Windows Bluetooth and privacy status APIs for connected-device and mic/camera alerts
- Win32 clipboard format listener for the expanded-panel clipboard history
- Windows performance counters, memory status, and network interface counters for expanded-panel system stats
- `HttpClient` for user-provided ICS subscription URLs with conditional GET caching

WPF is the first implementation because it gives direct transparent-window and desktop interop support with a simple CLI build/run loop.

## Setup From Source

Prerequisites:

- Windows 10 version 2004 or newer, or Windows 11
- Git
- .NET 8 SDK for build, run, test, and local publish
- .NET 8 Desktop Runtime only if you run a framework-dependent publish on a machine without the SDK

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

Hover the notch to expand it. The control center changes the current output device, tracks the master volume on the selected output, exposes active per-app audio sessions with volume/mute controls, toggles the default microphone mute, and shows brightness sliders for displays that support WMI or DDC/CI brightness. The Focus section starts 25/5, 50/10, or custom 1..180 minute focus timers, with optional auto-cycle; active timers stay visible in the compact pill and survive restart from `%LOCALAPPDATA%\Winotch\focus-timer.json`. Media buttons control the focused Windows media session in the expanded capsule and in the brief media toast. Notification toasts show app/sender text, time, and available live Windows action buttons when the OS exposes them. Priority status toasts appear for focus completions, low battery, charger connect/disconnect, Wi-Fi loss/reconnect, Bluetooth device connect, and mic/camera activity. The expanded panel also shows a small clipboard history with text, links, image thumbnails, and copied file lists. Wi-Fi connect works for saved Windows Wi-Fi profiles.

On multi-monitor setups, the notch follows the monitor containing the foreground app. When the desktop or shell has focus, it follows the monitor containing the cursor, falling back to the last monitor and then the primary monitor if needed. Settings can disable active-monitor following and keep the notch on the primary monitor.

The Calendar settings group accepts one or more `webcal://`, `https://`, or `http://` ICS URLs, refreshes them every five minutes, and adds the next three 24-hour agenda items plus Join buttons for Zoom, Teams, and Google Meet links.

The tray icon opens Settings, pauses/resumes the overlay, toggles Start with Windows, and exits the app. Settings changes apply live and persist as indented JSON at `%LOCALAPPDATA%\Winotch\settings.json`; corrupt JSON is moved aside as `settings.bad.json` and defaults are used. Settings can disable clipboard capture, per-app mixer, stats sampling, and active-monitor following without restarting.

Charger-connect priority toasts add a compact green battery-fill flourish with a prominent percent readout. Charger disconnect keeps the existing quieter status toast.

The expanded System column shows compact CPU, RAM, and network text values. Sampling starts only while the expanded panel is visible and stops again on collapse.

The camera button in the expanded control center opens a small live mirror flyout below the notch. The preview is mirrored by default, has a one-click normal-view toggle, and closes on X, Esc, outside click, notch collapse, pause, or power transition. Winotch never records or saves camera frames; the camera device is opened only for the live preview and released on close. The mirror uses the default Windows camera only; a camera picker is intentionally out of scope.

## Test Coverage

Run the full regression suite before sharing a build:

```powershell
dotnet test
```

The tests cover Wi-Fi parsing, battery fill/color thresholds, focus timer state transitions/persistence/formatting, ICS parsing/recurrence/timezone/join-link/countdown behavior, media toast geometry/timing and dedupe behavior, notification toast metadata/actions/dedupe behavior, clipboard history preview/privacy/dedupe behavior, priority status alert transitions, control-center naming/device/brightness/debounce state logic, system stats sampling/rate math/formatting, camera mirror lifecycle/layout/suppression behavior, settings persistence/startup helpers, shell mode/fullscreen heuristics, active-monitor selection, app-bar DPI conversion, refresh-rate normalization, and animation timing guards.

Charging flourish tests cover reusable fill-width math, animation parameter derivation, charger-alert mapping, full and low-percent edge cases, and existing low-battery queue ordering.

## Clipboard History

Winotch keeps the latest 10 clipboard items in memory while the app is running. It does not write clipboard history to disk, settings, logs, or roaming storage. That is the privacy default: closing Winotch clears its private clipboard history.

The listener skips clipboard updates marked with Windows privacy exclusion formats such as `ExcludeClipboardContentFromMonitorProcessing` or `CanIncludeInClipboardHistory = 0`. Image captures store only a small thumbnail, not the original full bitmap.

## Camera Mirror

The camera mirror uses `Windows.Media.Capture.MediaCapture` with CPU-backed frame reading and renders frames into WPF as an in-memory preview. If Windows reports no camera, access denial, or exclusive-use failure, the flyout shows a quiet inline message instead of retrying. Opening the mirror suppresses Winotch's own camera-in-use priority alert while the preview is active.

## Optional Local Publish

Winotch is currently an unpackaged desktop app with no published GitHub binary. If you want a local EXE from source, publish into a local folder.

Framework-dependent publish, smallest output:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -o "$env:LOCALAPPDATA\Winotch"
```

Self-contained publish, no separate .NET install needed:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$env:LOCALAPPDATA\Winotch"
```

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

Windows requires explicit user permission for notification listener access. Full all-app notification access also requires the Windows User Notification capability in a packaged app manifest. If access is not granted or unavailable in the unpackaged dev build, Winotch still watches live Windows toast windows where possible and shows the OS state in the notification panel instead of pretending history access is available.

Winotch respects the Windows notification state before showing its own compact notification toast. If Windows reports that notifications should be suppressed, including Do Not Disturb/quiet states, Winotch updates the notification list but does not pop a toast.

## Platform Notes

- Camera: Winotch opens the default Windows camera only while the live mirror flyout is visible. It does not record, persist frames, or offer a camera picker.
- Clipboard: clipboard history is in-memory only and clears when Winotch exits.
- Notifications: full notification history depends on Windows permission and packaged-app capabilities; the source-run alpha degrades quietly when those are unavailable.
- Calendar: Winotch fetches only user-provided ICS URLs and caches conditional GET metadata locally.
- Settings: local JSON state lives under `%LOCALAPPDATA%\Winotch`.
