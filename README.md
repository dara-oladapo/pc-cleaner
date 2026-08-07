# PC Cleaner

A free, no-paywall system cleanup utility for Windows and macOS (Linux planned). No subscriptions, no "upgrade to clean more than 500 MB" — everything in this repo is fully functional.

## Features

- **Junk Cleanup** — finds temp files, browser caches, OS logs, and package manager caches (npm/pip/NuGet/Homebrew), reports reclaimable size, deletes what you select.
- **Duplicates & Large Files** — scans folders you choose (plus sensible defaults), groups duplicates by content hash, moves selected copies to the Recycle Bin / Trash (never a hard delete).
- **Startup Manager** — lists apps that launch automatically (Registry Run keys + Startup folder on Windows; LaunchAgents/LaunchDaemons on macOS) and lets you enable/disable them reversibly.

## Architecture

```
src/
  PcCleaner.Core/        Platform-agnostic: models, interfaces, duplicate/large-file scanning, junk deletion logic
  PcCleaner.App/          .NET MAUI app (UI, MVVM, DI)
    Platforms/Windows/    Windows-specific: junk locations, Recycle Bin (SHFileOperation), registry startup items
    Platforms/MacCatalyst/  macOS-specific: junk locations, Trash (NSFileManager), LaunchAgent/Daemon management
tests/
  PcCleaner.Core.Tests/   xUnit tests for the cross-platform Core logic
```

The rule of thumb: anything that's pure `System.IO` logic (recursive file walking, hashing, size math) lives in `PcCleaner.Core` and is unit-tested there. Anything that touches an OS API (registry, Recycle Bin, `launchctl`, native Trash) lives under `Platforms/<OS>` in the MAUI project and is resolved via DI in `MauiProgram.cs`.

Junk/cache files are deleted outright (they're regenerable). Files found by the Duplicate/Large File finder are real user files, so they're moved to the Recycle Bin / Trash instead — reversible by design.

## Requirements

- .NET 10 SDK (this repo pins the SDK via `global.json`)
- `maui-windows` and `maccatalyst` workloads: `dotnet workload install maui-windows maccatalyst` (Windows) — on macOS you'd install `maui-maccatalyst` from a Mac with Xcode installed.

## Building

Each platform target must be built explicitly — a plain `dotnet build` on the solution will try (and, on a machine missing the other platform's toolchain, fail) to build for a platform you can't run:

```powershell
# Windows
dotnet build src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-windows10.0.19041.0

# macOS (must be run on a Mac with Xcode installed)
dotnet build src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-maccatalyst
```

Run the whole solution (each project restricted to its own supported frameworks) with `dotnet build PcCleaner.slnx`.

Run tests:

```powershell
dotnet test tests/PcCleaner.Core.Tests/PcCleaner.Core.Tests.csproj
```

## macOS: App Sandbox is disabled on purpose

`Platforms/MacCatalyst/Entitlements.plist` ships with `com.apple.security.app-sandbox = false`. A sandboxed app cannot read another app's caches, manage LaunchAgents outside its own container, or browse arbitrary user folders — all of which this app needs to do. This is standard for the category (CleanMyMac, DaisyDisk, and OnyX are all unsandboxed) and means the app must be **notarized and distributed directly**, not through the Mac App Store, which requires sandboxing.

## Known gaps / next steps

- **Linux has no path here.** Standard .NET MAUI does not support Linux as a target at all — there's no workload for it. `PcCleaner.Core` was deliberately kept 100% platform-agnostic so it can be reused; getting to Linux means pairing it with a different UI layer (e.g. Avalonia) and writing a `Platforms/Linux`-equivalent set of services (junk paths, trash via `gio trash`/freedesktop trash spec, systemd user units / XDG autostart for the startup manager).
- **Folder picker** for the Duplicate Finder is a plain text field (paste a path) rather than a native folder browser — `CommunityToolkit.Maui`'s `FolderPicker` would add this, but its current version requires a newer `Microsoft.Maui.Controls` than this SDK's `maui-windows`/`maccatalyst` workload ships, causing a version-downgrade conflict. Revisit once the workload catches up.
- **Recycle Bin / Trash failures on system-owned junk paths** (e.g. `C:\Windows\SoftwareDistribution\Download`, `/Library/Caches`) are expected without elevated privileges — the cleaner reports these as partial failures rather than crashing, but doesn't yet prompt for elevation.
- Startup item disabling for **system-scope** entries (`HKLM` Run keys on Windows, `/Library/LaunchAgents`, `/Library/LaunchDaemons` on macOS) requires admin/root and will throw; the UI surfaces the error but doesn't yet offer an elevation prompt.
