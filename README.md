# Winotch

Winotch is a native Windows notch overlay. It stays centered at the top of the primary screen and shows time, date, battery, Wi-Fi, volume, current media, and Windows notifications in a compact black shell that expands on hover. Media track changes, unsilenced Windows notifications, and priority system status changes use brief compact toasts.

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

WPF is the first implementation because it gives direct transparent-window and desktop interop support with a simple CLI build/run loop.

## Run

Prerequisites:

- Windows 10 2004 or newer, or Windows 11
- .NET 8 SDK for development
- .NET 8 Desktop Runtime for framework-dependent installs

From the repository root:

```powershell
dotnet run --project src/Winotch/Winotch.csproj
```

Hover the notch to expand it. The control center changes the current output device, tracks the master volume on the selected output, exposes active per-app audio sessions with volume/mute controls, toggles the default microphone mute, and shows brightness sliders for displays that support WMI or DDC/CI brightness. Media buttons control the focused Windows media session in the expanded capsule and in the brief media toast. Notification toasts show app/sender text, time, and available live Windows action buttons when the OS exposes them. Priority status toasts appear for low battery, charger connect/disconnect, Wi-Fi loss/reconnect, Bluetooth device connect, and mic/camera activity. Wi-Fi connect works for saved Windows Wi-Fi profiles.

## Test

Run the full regression suite before sharing a build:

```powershell
dotnet test Winotch.slnx
```

The tests cover Wi-Fi parsing, battery fill/color thresholds, media toast geometry/timing and dedupe behavior, notification toast metadata/actions/dedupe behavior, priority status alert transitions, control-center naming/device/brightness/debounce state logic, shell mode/fullscreen heuristics, app-bar DPI conversion, refresh-rate normalization, and animation timing guards.

## Install

Winotch is currently an unpackaged desktop app. Use a publish folder as the install artifact.

Framework-dependent publish, smallest output:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -o "$env:LOCALAPPDATA\Winotch"
```

Self-contained publish, no separate .NET install needed:

```powershell
dotnet publish src/Winotch/Winotch.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "$env:LOCALAPPDATA\Winotch"
```

Run the installed app:

```powershell
& "$env:LOCALAPPDATA\Winotch\Winotch.exe"
```

Add Winotch to startup:

```powershell
$startup = [Environment]::GetFolderPath("Startup")
$target = "$env:LOCALAPPDATA\Winotch\Winotch.exe"
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut("$startup\Winotch.lnk")
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = Split-Path $target
$shortcut.Save()
```

Uninstall:

```powershell
Remove-Item "$([Environment]::GetFolderPath("Startup"))\Winotch.lnk" -ErrorAction SilentlyContinue
Remove-Item "$env:LOCALAPPDATA\Winotch" -Recurse -Force
```

## Notification Access

Windows requires explicit user permission for notification listener access. Full all-app notification access also requires the Windows User Notification capability in a packaged app manifest. If access is not granted or unavailable in the unpackaged dev build, Winotch still watches live Windows toast windows where possible and shows the OS state in the notification panel instead of pretending history access is available.

Winotch respects the Windows notification state before showing its own compact notification toast. If Windows reports that notifications should be suppressed, including Do Not Disturb/quiet states, Winotch updates the notification list but does not pop a toast.
