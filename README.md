# Winotch

Winotch is a native Windows notch overlay. It stays centered at the top of the primary screen and shows time, date, battery, Wi-Fi, volume, current media, and Windows notifications in a compact black shell that expands on hover, media playback, or notification activity.

## Stack

- C# WPF on `net8.0-windows10.0.19041.0`
- Transparent, topmost desktop window for the notch shell
- Windows Forms power status for battery
- Core Audio COM interop for system volume
- Windows system media transport controls for current audio metadata, artwork, and playback actions
- `netsh wlan` for Wi-Fi status, network listing, and saved-profile connect attempts
- `UserNotificationListener` for Windows toast notification access when the OS grants permission

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

Hover the notch to expand it. The volume slider changes the system master volume. Media buttons control the focused Windows media session. Wi-Fi connect works for saved Windows Wi-Fi profiles.

## Test

Run the full regression suite before sharing a build:

```powershell
dotnet test Winotch.slnx
```

The tests cover Wi-Fi parsing, battery fill/color thresholds, media pop-up dedupe behavior, notification pop-up dedupe behavior, shell mode/fullscreen heuristics, app-bar DPI conversion, refresh-rate normalization, and animation timing guards.

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

Windows requires explicit user permission for notification listener access. Full all-app notification access also requires the Windows User Notification capability in a packaged app manifest. If access is not granted or unavailable in the unpackaged dev build, Winotch shows the OS state in the notification panel instead of pretending notifications are available.
