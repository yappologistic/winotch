# Winotch Agent Guide

## Project
Winotch is a native Windows desktop notch: a centered, top-attached Fluent overlay inspired by the MacBook notch. It shows time, date, battery, Wi-Fi, volume, media, and recent notifications with polished motion.

## Stack
- Use C# and WinUI 3 on `net8.0-windows10.0.26100.0`, backed by the stable Windows App SDK 2.2 release.
- Use WinUI `SystemBackdropElement`/`DesktopAcrylicBackdrop` for the compact overlay and Settings. Do not replace native materials with simulated blur.
- Use `Microsoft.UI.Windowing.AppWindow` for placement, visibility, switcher presence, and presenter behavior. Keep narrowly scoped Win32 interop for HWND-only behavior such as rounded window regions, ownership, drag, hotkeys, and clipboard messages.
- Prefer Windows built-in APIs and .NET libraries before adding packages.
- Keep the alpha app unpackaged and source-only. If full notification history is required, use an explicitly approved package-identity approach; `UserNotificationListener` access cannot be assumed for an unpackaged process.

## Commands
- Build: `dotnet build`
- Run: `dotnet run --project src/Winotch/Winotch.csproj`
- Test: `dotnet test`
- CI parity: `dotnet restore`, `dotnet build --no-restore -warnaserror`, `dotnet test --no-build --verbosity normal`

## Design
- Keep design tokens in one place and reuse them.
- Use Segoe UI Variable / Segoe UI first; it is the closest native Windows equivalent to Apple's San Francisco without bundling proprietary fonts.
- Keep the notch centered across resolution changes and monitor changes.
- Preserve the reference geometry: Mini is `260 x 68` DIPs; Live and compact media/toast surfaces are `440 x 76` DIPs.
- Treat Desktop Acrylic, translucent blue-gray contrast layers, subtle white strokes, rounded geometry, and Fluent control states as one consistent visual system across shell modes and auxiliary flyouts.
- Prefer small, focused components over speculative architecture.

## Git
- Do not commit build outputs, user settings, logs, packages, or local IDE files.
- Do not commit screenshots, videos, or locally published EXE folders.
- Do not commit QA screenshots, screen recordings, captured clipboard images, or temporary Codex/agent artifacts.
- Before GitHub publication, scan tracked files and history for local artifacts; rewrite history only when artifacts are actually reachable.
- Commit coherent milestones: project setup, core shell/UI, OS integrations, docs/verification.
- Keep GitHub docs source-only while alpha: no release workflows, tags, or published binaries unless explicitly requested.

## Privacy
- Document clipboard, notification, camera, and local settings behavior plainly before changing those surfaces.
- Do not add telemetry or network calls beyond user-provided calendar URLs without explicit approval.
