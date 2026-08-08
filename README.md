# PC Cleaner

A free, no-paywall system cleanup utility for Windows and macOS (Linux planned). No subscriptions, no "upgrade to clean more than 500 MB" — everything in this repo is fully functional.

## Features

- **Dashboard** — opens on your drive drawn as a single capacity bar (occupied / reclaimable / free) and one "Scan everything" button that runs every tool in one pass, with a summary card per tool.
- **Junk Cleanup** — finds temp files, browser caches, OS logs, and package manager caches (npm/pip/NuGet/Homebrew), groups them by category, reports reclaimable size, and deletes what you select after confirming.
- **Duplicates** — scans folders you choose (plus sensible defaults), groups files by content hash, and moves selected copies to the Recycle Bin / Trash (never a hard delete).
- **Large Files** — lists everything over 100 MB in those folders, biggest first, with the date each file last changed.
- **Startup Manager** — lists apps that launch automatically (Registry Run keys + Startup folder on Windows; LaunchAgents/LaunchDaemons on macOS) and lets you enable/disable them reversibly.
- **About / Updates** — shows the installed version and checks GitHub Releases for a newer one via [Velopack](https://velopack.io); download-and-restart happens in-app, no browser round-trip.

Nothing is deleted without a confirmation that says what will go and whether it can be recovered. Junk is deleted permanently (caches are regenerable, and filling the Recycle Bin with them defeats the point); your own files always go to the Recycle Bin / Trash instead.

## Architecture

```
src/
  PcCleaner.Core/        Platform-agnostic: models, interfaces, duplicate/large-file scanning, junk deletion logic
  PcCleaner.App/          .NET MAUI app (UI, MVVM, DI)
    Platforms/Windows/    Windows-specific: junk locations, Recycle Bin (SHFileOperation), registry startup items
    Platforms/MacCatalyst/  macOS-specific: junk locations, Trash (NSFileManager), LaunchAgent/Daemon management
    Services/             UpdateService (Velopack), DialogService (confirmations), IFolderPickerService
    Controls/             IconFlyoutItem — carries nav-rail icon geometry through Shell's item template
    Resources/Styles/     Colors, Tokens, Icons, Styles — the design system (see DESIGN.md)
tests/
  PcCleaner.Core.Tests/   xUnit tests for the cross-platform Core logic
docs/
  prototype/index.html    Clickable prototype of every screen — the design reference for the MAUI UI
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

## Releases & auto-update

Installers are built with [Velopack](https://velopack.io) (`vpk` CLI) — it packages a self-contained publish into a real installer (`Setup.exe` on Windows, `.pkg`/`.zip` on macOS) and is what `UpdateService` (`Services/UpdateService.cs`) talks to at runtime to check GitHub Releases for a newer version and apply it. `VelopackApp.Build().Run()` runs as the very first line of `MauiProgram.CreateMauiApp()` — it intercepts the installer's `--veloapp-install` / `--veloapp-uninstall` lifecycle hooks and exits immediately for those, before any UI would start.

Install the CLI once: `dotnet tool install -g vpk`.

**Windows** (verified working end-to-end — build, install, version detection, and clean uninstall all confirmed):

```powershell
dotnet publish src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained -o publish/win-x64

vpk pack `
  --packId PCCleaner --packVersion 1.0.0 `
  --packDir publish/win-x64 --mainExe PcCleaner.App.exe `
  --icon publish/win-x64/appicon.ico `
  --packTitle "PC Cleaner" --packAuthors "Dara Oladapo" `
  --outputDir releases/win
```

Produces `releases/win/PCCleaner-win-Setup.exe` plus the `.nupkg`/`RELEASES`/`releases.win.json` files `UpdateService` looks for.

**macOS** (documented, not yet run — needs a Mac with Xcode; same gap as the Mac Catalyst build itself, see [#4](https://github.com/dara-oladapo/pc-cleaner/issues/4)):

```bash
dotnet publish src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-maccatalyst -c Release --self-contained -o publish/osx

vpk pack \
  --packId PCCleaner --packVersion 1.0.0 --channel osx \
  --packDir publish/osx --mainExe PcCleaner.App \
  --icon Resources/AppIcon/appicon.icns \
  --packTitle "PC Cleaner" --packAuthors "Dara Oladapo" \
  --outputDir releases/osx
```

(`appicon.icns` doesn't exist yet — MAUI's Windows build auto-generates an `.ico` at publish time, but the macOS `.icns` needs to be produced separately, e.g. via `iconutil` on a Mac.)

**Publishing a release:** create a GitHub Release tagged with the version (e.g. `v1.0.0`) on the repo and upload everything from `releases/win/` (and `releases/osx/` once that exists) as release assets — `UpdateService` points at `https://github.com/dara-oladapo/pc-cleaner` via Velopack's `GithubSource` and reads the release feed from there. Until a release is published, "Check for Updates" will always report up to date (there's nothing to compare against yet).

## macOS: App Sandbox is disabled on purpose

`Platforms/MacCatalyst/Entitlements.plist` ships with `com.apple.security.app-sandbox = false`. A sandboxed app cannot read another app's caches, manage LaunchAgents outside its own container, or browse arbitrary user folders — all of which this app needs to do. This is standard for the category (CleanMyMac, DaisyDisk, and OnyX are all unsandboxed) and means the app must be **notarized and distributed directly**, not through the Mac App Store, which requires sandboxing.

## Known gaps / next steps

- **Linux has no path here.** Standard .NET MAUI does not support Linux as a target at all — there's no workload for it. `PcCleaner.Core` was deliberately kept 100% platform-agnostic so it can be reused; getting to Linux means pairing it with a different UI layer (e.g. Avalonia) and writing a `Platforms/Linux`-equivalent set of services (junk paths, trash via `gio trash`/freedesktop trash spec, systemd user units / XDG autostart for the startup manager).
- **Folder picking** uses the native OS browser (`Windows.Storage.Pickers.FolderPicker` on Windows, `UIDocumentPickerViewController` on macOS) via `IFolderPickerService`, with the typed-path entry kept as a fallback. `CommunityToolkit.Maui`'s `FolderPicker` is still unusable here — its current version requires a newer `Microsoft.Maui.Controls` than this SDK's `maui-windows`/`maccatalyst` workload ships, causing a version-downgrade conflict — hence the hand-rolled platform services.
- **No pull-request build.** `.github/workflows/release.yml` only runs on tags, so UI changes get no compile check before merge. A `windows-latest` job on `pull_request` would catch XAML errors that `MauiXamlInflator=SourceGen` surfaces at build time.
- **Recycle Bin / Trash failures on system-owned junk paths** (e.g. `C:\Windows\SoftwareDistribution\Download`, `/Library/Caches`) are expected without elevated privileges — the cleaner reports these as partial failures rather than crashing, but doesn't yet prompt for elevation.
- Startup item disabling for **system-scope** entries (`HKLM` Run keys on Windows, `/Library/LaunchAgents`, `/Library/LaunchDaemons` on macOS) requires admin/root and will throw; the UI surfaces the error but doesn't yet offer an elevation prompt.
