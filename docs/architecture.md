# Winotch Architecture

## Runtime Flow

```mermaid
flowchart TD
    App["WPF App"] --> Window["Transparent Topmost Notch Window"]
    Window --> Clock["Clock Timer"]
    Window --> Status["Status Timer"]
    Status --> Battery["Windows Power Status"]
    Status --> Audio["Core Audio Endpoints and Sessions"]
    Status --> Media["Windows Media Sessions"]
    Status --> Wifi["netsh wlan"]
    Status --> Notifications["UserNotificationListener"]
    Status --> Priority["Priority Status Alerts"]
    Status --> Brightness["WMI and DDC/CI Brightness"]
    Battery --> Window
    Audio --> Window
    Media --> Window
    Wifi --> Window
    Notifications --> Window
    Priority --> Window
    Brightness --> Window
```

## UI System

```mermaid
flowchart LR
    Tokens["App.xaml Tokens"] --> Shell["Notch Shell"]
    Tokens --> Chips["Status Chips"]
    Tokens --> Panel["Expanded Panel"]
    Shell --> Compact["Compact State"]
    Shell --> Expanded["Expanded State"]
    Expanded --> Media["Now Playing Controls"]
    Shell --> MediaToast["Compact Media Toast"]
    Shell --> NotificationToast["Compact Notification/Status Toast"]
    Expanded --> Notifications["Notifications"]
    Expanded --> Controls["Control Center"]
```

## Design Tokens

- `NotchBlack`: shell background
- `NotchPanel`: chip/control background
- `NotchText`: primary text
- `NotchMutedText`: secondary text
- Typography: Segoe UI Variable Text, falling back to Segoe UI
- Icons: Segoe MDL2 Assets

## Motion

The resting notch is a compact top-attached pill. Hover expands width and height with WPF-native property animations. Detail content begins fading in during the geometry morph, while the header/status layout switches after the shell settles so it does not jump mid-transition. Media, notification, and priority status events use the compact toast geometry instead of opening the full expanded panel.

Animation timings live in `ShellAnimationTiming`:

- `MotionMilliseconds`: width, height, and left-position transition duration.
- `FadeMilliseconds`: detail/header fade duration.
- `DetailRevealDelayMilliseconds`: delay before the expanded panel begins fading in during the geometry morph.
- `CollapseGuardMilliseconds`: pointer-exit delay. It intentionally outlasts the geometry motion so a brief hover miss cannot cancel expansion halfway through.

## Shell States

- `Mini`: tiny centered pill for desktop/idle context.
- `FullBar`: full-width top bar when the foreground app is maximized or fills the screen.
- `Expanded`: larger centered island on hover.
- `Compact Toast`: centered transient capsule for media track changes, unsilenced notification arrivals, and priority status alerts.

Foreground detection uses Win32 window bounds/window placement and falls back to `Mini` for the desktop shell and Winotch's own window. When Winotch owns foreground, fallback app-window scanning ignores shell, hidden, minimized, own, and tiny utility windows so minimized apps do not force the full-width bar.

## Media

Winotch reads the focused Windows system media transport session through `GlobalSystemMediaTransportControlsSessionManager`. The expanded capsule keeps artwork, title, artist, and previous/play-pause/next controls. New playing tracks also show a brief compact toast with the same controls, then return to the normal mini/full-bar shell so fullscreen apps are not covered by the full expanded capsule.

## Control Center

The expanded panel control center is backed by small services around Windows APIs. `AudioDeviceService` enumerates active render endpoints, marks the current default, and switches all default roles through PolicyConfig. `AudioService` re-resolves cached endpoints when the system default changes so the master slider follows the newly selected output. `AudioSessionService` reads active render sessions through `IAudioSessionManager2`, resolves app labels from process metadata and session fallbacks, and applies per-session volume/mute through `ISimpleAudioVolume`.

The microphone row toggles mute on the default capture endpoint and shares the same privacy active-use signal used by priority status alerts. Brightness uses WMI for internal panels and DDC/CI for external monitors; unsupported or failing monitors are omitted, and writes run off the UI thread through the debounced control-center writer.

## Notifications

Winotch reads notification history through `UserNotificationListener` when Windows grants access and also watches live Windows toast windows through UI Automation in unpackaged builds. New unsilenced notifications show a compact toast with app/sender text, message body, time, app icon when available, and up to two live action buttons when Windows exposes invokable toast actions. `SHQueryUserNotificationState` and the global toast toggle gate Winotch's own popups so Do Not Disturb/quiet states do not create duplicate interruption.

## Priority Status Alerts

Priority status alerts reuse the compact notification toast surface for system events that should be glanceable without opening the full capsule: low battery, charger connect/disconnect, Wi-Fi loss/reconnect, Bluetooth device connect, and mic/camera activation. Battery and Wi-Fi reuse the existing status reads. Bluetooth uses the native Windows Bluetooth device enumeration API, while mic/camera activity comes from Windows privacy usage registry state. The tracker suppresses routine first-run connection state and repeated low-battery spam, but queues simultaneous critical alerts such as camera, microphone, and low battery.

## Test Strategy

The automated suite focuses on deterministic logic that would otherwise surface as visual bugs:

- Wi-Fi netsh/profile parsing, de-duplication, blank values, and visible list limits.
- Battery icon fill width, clamp behavior, charging color, and low-power thresholds.
- Media snapshot display fallbacks, artwork fallback, compact toast geometry/timing, and track-change de-duplication.
- Notification signature generation, first-run suppression, empty snapshot behavior, repeated-message handling, shell suppression mapping, compact toast metadata, and live action invocation.
- Priority status transition handling for low battery, charger changes, Wi-Fi loss/reconnect, Bluetooth connects, mic/camera activation, queued alerts, and privacy active-use detection.
- Control-center app naming fallbacks, output device ordering/default marking, microphone pill state mapping, brightness normalization/clamping, and debounced brightness writes.
- Foreground mode heuristics for desktop, own window, maximized apps, screen-filling apps, and near-threshold windows.
- Fallback app-window filtering so hidden, minimized, shell, own, and tiny windows cannot force full-bar mode.
- App-bar DIP-to-physical-pixel conversion across DPI scales.
- Display refresh-rate normalization for high-refresh monitors and invalid OS values.
- Shell metrics and timing guards for centered mini/expanded states and non-interrupted hover expansion.
