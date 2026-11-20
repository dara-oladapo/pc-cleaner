# PC Cleaner

PC Cleaner, by [WiseLead Solutions](https://github.com/wiselead-solutions), is a free and open source system cleanup utility for Windows and macOS (Linux in progress — an AppImage is built, but most Linux services are still stubs). No subscriptions, no "upgrade to clean more than 500 MB", no license keys. Everything in this repo is fully functional, and it is licensed under the [Apache License 2.0](LICENSE).

## Features

- **Dashboard** — opens on your drive drawn as a single capacity bar (occupied / reclaimable / free) with a row per tool and a row per drive or volume, each with a tick. A tool's row expands to its own parts (junk categories, duplicate groups, large files, cloud folders, developer actions), ticked the same way as on the tool's page. Three actions run whatever is ticked: **Scan everything** reads and deletes nothing, **Scan and clean** asks once for all tools and then cleans, and **Scan and auto clean** cleans without asking.
- **Junk Cleanup** — finds temp files, browser caches, OS logs, and package manager caches (npm/pip/NuGet/Homebrew), developer tool caches (Yarn, pnpm, Gradle, Cargo, Go, VS Code, JetBrains, Playwright, Xcode DeviceSupport), groups them by category, reports reclaimable size, and deletes what you select after confirming. Large, slow-to-rebuild stores (NuGet global packages, Gradle, Cargo, pnpm, Playwright browsers, Xcode DeviceSupport, JetBrains indexes) are listed but left unticked.
- **Duplicates** — scans folders you choose (plus sensible defaults), groups files by content hash, and moves selected copies to the Recycle Bin / Trash (never a hard delete).
- **Large Files** — lists everything over 100 MB in those folders, biggest first, with the date each file last changed. A file inside a cloud drive (OneDrive, Dropbox, iCloud Drive and so on) is marked **Cloud only**: it is listed, it cannot be ticked, and it is never deleted here, by either Large Files or Duplicates. To free space held by a cloud drive, use **Cloud Drives**, which makes files online-only and deletes nothing.
- **Volumes to scan** (Settings) — choose which local volumes Duplicates and Large Files look at. Only the system volume by default; network, optical, cloud-drive and system/snapshot mounts are never offered.
- **Developer cleanup** (opt-in) — for anyone who writes code. Off by default; turn on **Developer mode** in Settings (the app suggests it once if git or Docker is installed) and a Developer page appears. It finds Docker build cache, unused images and stopped containers (on the host, and on Windows inside running WSL distros), git worktrees that are clean, fully pushed and idle for 14 days, and git-ignored build output (`bin`/`obj`, `node_modules`, `target`, `.venv`...) of projects untouched for 30 days. It looks in your code folders (`~/source/repos`, `~/repos`, `~/src`, `~/code`, `~/Projects`, `~/Dev`, `~/Documents/GitHub`, `~/Developer` on macOS), which you can change in Settings. Worktrees are removed only with `git worktree remove` (never forced, branches kept), Docker volumes are never ticked for you, and every run asks first. While developer mode is off the app never runs docker, wsl or git.
- **Startup Manager** — lists apps that launch automatically (Registry Run keys + Startup folder on Windows; LaunchAgents/LaunchDaemons on macOS) and lets you enable/disable them reversibly.
- **Appearance** — Light, Dark, or System (the default), chosen from the navigation rail and remembered across restarts. System keeps following the OS rather than sampling it once at launch.
- **About / Updates** — shows the installed version and checks GitHub Releases for a newer one via [Velopack](https://velopack.io); download-and-restart happens in-app, no browser round-trip.

Nothing is deleted without a confirmation that says what will go and whether it can be recovered, with one exception you choose each time: the dashboard's **Scan and auto clean**. It removes exactly what is ticked after the scan and nothing else. The default ticks are the same as everywhere else in the app: large files start unticked, one copy of every duplicate is kept, the large slow-to-rebuild caches and Docker volumes stay unticked, and cloud files are made online-only, never deleted. A scan that was stopped never goes on to clean. Junk is deleted permanently (caches are regenerable, and filling the Recycle Bin with them defeats the point); your own files always go to the Recycle Bin / Trash instead.

## Architecture

```
src/
  PcCleaner.Core/        Platform-agnostic: models, interfaces, duplicate/large-file scanning, junk deletion logic
  PcCleaner.Presentation/ UI-framework-agnostic (plain net10.0): view models, plus the service interfaces they use
    ViewModels/           Every view model (CommunityToolkit.Mvvm) — shared by the MAUI app and a future Linux front end
    Services/             IDialogService, IFolderPickerService, IThemeService, INavigationService, IUpdateService
  PcCleaner.App/          .NET MAUI app (XAML, MAUI service implementations, DI)
    Platforms/Windows/    Windows-specific: junk locations, Recycle Bin (SHFileOperation), registry startup items
    Platforms/MacCatalyst/  macOS-specific: junk locations, Trash (NSFileManager), LaunchAgent/Daemon management
    Services/             UpdateService (Velopack), DialogService (confirmations), ThemeService, ShellNavigationService
    Controls/             IconFlyoutItem — carries nav-rail icon geometry through Shell's item template
    Resources/Styles/     Colors, Tokens, Icons, Styles — the design system (see DESIGN.md)
  PcCleaner.Desktop/      Avalonia front end for Linux (plain net10.0, builds on every OS). Every screen, same view models
    Platforms/Linux/      Stubs that touch nothing until #10-#13 land: junk scanner, trash, startup items, folder picker, theme
    Services/             DialogService (in-app overlay), ShellNavigationService, UpdateService (Velopack, AppImage only)
    Resources/Styles/     The design system ported to Avalonia: Colors (Light/Dark theme dictionaries), Tokens, Icons, Styles
    Views/                One view per screen, plus DialogHostView and ScanRootsView
packaging/
  linux/                  Linux app icon (hicolor theme layout) and .desktop entry for menu integration
tests/
  PcCleaner.Core.Tests/   xUnit tests for the cross-platform Core logic
docs/
  prototype/index.html    Clickable prototype of every screen — the design reference for the MAUI UI
```

The rule of thumb: anything that's pure `System.IO` logic (recursive file walking, hashing, size math) lives in `PcCleaner.Core` and is unit-tested there. Anything that touches an OS API (registry, Recycle Bin, `launchctl`, native Trash) lives under `Platforms/<OS>` in the MAUI project and is resolved via DI in `MauiProgram.cs`. View models live in `PcCleaner.Presentation` and reach the UI only through its service interfaces; that project must never reference MAUI or Avalonia, and the Ubuntu CI job fails if it does.

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

Linux, through the Avalonia front end. Its junk scanner is still a stub that finds nothing (#10), so a Debug build can fill the list with synthetic results instead. They are invented: nothing on disk is read or deleted.

```bash
dotnet build src/PcCleaner.Desktop/PcCleaner.Desktop.csproj
PCCLEANER_FAKE_SCAN=1 dotnet run --project src/PcCleaner.Desktop
```

Run tests:

```powershell
dotnet test tests/PcCleaner.Core.Tests/PcCleaner.Core.Tests.csproj
```

## Releases & auto-update

Installers are built with [Velopack](https://velopack.io) (`vpk` CLI) — it packages a self-contained publish into a real installer (`Setup.exe` on Windows, `.pkg`/`.zip` on macOS) and is what `UpdateService` (`Services/UpdateService.cs`) talks to at runtime to check GitHub Releases for a newer version and apply it. `VelopackApp.Build().Run()` runs as the very first line of `MauiProgram.CreateMauiApp()` — it intercepts the installer's `--veloapp-install` / `--veloapp-uninstall` lifecycle hooks and exits immediately for those, before any UI would start. The Linux app does the same in `PcCleaner.Desktop/Program.cs`, and has its own `Services/UpdateService.cs` (see "Linux packaging" below).

Install the CLI once: `dotnet tool install -g vpk`.

### Continuous integration

`.github/workflows/ci.yml` runs on every pull request and every push to `main`, across all three desktop platforms. A change has to be green on all of them — the `All platforms green` job is the single status to require in branch protection.

| Runner | What it does |
|---|---|
| `ubuntu-latest` | Core build and tests, the `PcCleaner.Presentation` no-MAUI/no-Avalonia dependency check, and the Linux app build (`PcCleaner.Desktop`, Avalonia, compiled bindings). The MAUI project is never touched: its `TargetFrameworks` evaluates to empty on Linux. |
| `windows-latest` | Core tests, plus the Windows app build. The primary supported platform. |
| `xcode-27` | Core tests, plus the Mac Catalyst app build with Xcode 27.2. Not `macos-latest`: its newest Xcode (26.6) produces an app that crashes at launch on macOS 27. |

The app build step is the important one: `MauiXamlInflator=SourceGen` compiles XAML to C#, so a mistyped `StaticResource` key or a binding path that doesn't exist on its `x:DataType` fails the build rather than surfacing at runtime. The Avalonia app gets the same guarantee from compiled bindings.

Core is tested on all three rather than just once, deliberately — it's pure `System.IO`, which is exactly the code whose behaviour varies by platform (path roots, separators, drive enumeration). Testing it in one place would exercise it where it's least likely to break.

`.github/workflows/release.yml` is separate and only runs on tags.

**Windows** (verified working end-to-end — build, install, version detection, and clean uninstall all confirmed):

```powershell
dotnet publish src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-windows10.0.19041.0 -c Release --self-contained -o publish/win-x64

vpk pack `
  --packId PCCleaner --packVersion 1.0.0 `
  --packDir publish/win-x64 --mainExe PcCleaner.App.exe `
  --icon publish/win-x64/appicon.ico `
  --packTitle "PC Cleaner" --packAuthors "WiseLead Solutions" `
  --outputDir releases/win
```

Produces `releases/win/PCCleaner-win-Setup.exe` plus the `.nupkg`/`RELEASES`/`releases.win.json` files `UpdateService` looks for.

**macOS** (build and pack verified on an Apple Silicon Mac — macOS 27, Xcode 27.2; the installed app's runtime behaviour is still tracked in [#4](https://github.com/wiselead-oss/pc-cleaner/issues/4)):

```bash
# Release builds both maccatalyst-arm64 and maccatalyst-x64 and merges them into one universal bundle:
# src/PcCleaner.App/bin/Release/net10.0-maccatalyst/PC Cleaner.app
dotnet publish src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-maccatalyst -c Release \
  -p:CreatePackage=false -p:ApplicationDisplayVersion=1.0.0 -p:ApplicationVersion=1

# vpk wants a .entitlements file without the BOM the project's plist has.
plutil -convert xml1 -o /tmp/PcCleaner.entitlements src/PcCleaner.App/Platforms/MacCatalyst/Entitlements.plist

vpk pack \
  --packId PCCleaner --packVersion 1.0.0 --channel osx \
  --packDir "src/PcCleaner.App/bin/Release/net10.0-maccatalyst/PC Cleaner.app" --mainExe PcCleaner.App \
  --packTitle "PC Cleaner" --packAuthors "WiseLead Solutions" \
  --signAppIdentity=- --signEntitlements /tmp/PcCleaner.entitlements \
  --outputDir releases/osx
```

`--packDir` is the `.app` itself, so vpk keeps MAUI's bundle (icon included) and only adds its updater. `--signAppIdentity=-` signs ad-hoc; swap in a Developer ID identity (plus `--signInstallIdentity` and `--notaryProfile`) for a signed, notarized build. Produces `releases/osx/PCCleaner-osx-Setup.pkg`, `PCCleaner-osx-Portable.zip`, `PCCleaner-1.0.0-osx-full.nupkg`, `releases.osx.json`, `assets.osx.json` and `RELEASES-osx`.

**Linux** (the Avalonia app, `PcCleaner.Desktop`; verified on Ubuntu under WSL — publish, pack, and the AppImage's contents and update metadata, not yet a full update round-trip against a published release):

```bash
dotnet publish src/PcCleaner.Desktop/PcCleaner.Desktop.csproj -c Release -r linux-x64 --self-contained -p:Version=1.0.0 -o publish/linux-x64

vpk pack \
  --packId PCCleaner --packVersion 1.0.0 --channel linux --runtime linux-x64 \
  --packDir publish/linux-x64 --mainExe PcCleaner.Desktop \
  --icon packaging/linux/icons/hicolor/256x256/apps/pccleaner.png --categories Utility \
  --packTitle "PC Cleaner" --packAuthors "Dara Oladapo" \
  --outputDir releases/linux
```

Produces `releases/linux/PCCleaner.AppImage` plus `PCCleaner-1.0.0-linux-full.nupkg`, `RELEASES-linux`, `releases.linux.json` and `assets.linux.json`. `vpk pack` only builds AppImages on Linux, so this step can't run from Windows or macOS. Every file name carries the `linux` channel (the AppImage's name is unique on its own), so they sit in the same GitHub Release as the Windows and macOS files without colliding; the release workflow checks that before it uploads.

If the build fails with `This version of .NET for MacCatalyst (X.Y.…) requires Xcode X.Y`, either switch Xcode with `sudo xcode-select -s /Applications/Xcode_X.Y.app` or, for a minor-version mismatch, add `-p:ValidateXcodeVersion=false`.

**Publishing a release:** push a `vX.Y.Z` tag. `.github/workflows/release.yml` builds all three platforms and creates a single GitHub Release containing every set of files. Windows, macOS and Linux live side by side in it as separate Velopack channels (`win`, `osx` and `linux`), and every file name carries its channel, so they never overwrite each other. `UpdateService` points at `https://github.com/wiselead-oss/pc-cleaner` via Velopack's `GithubSource`, and each installed copy only reads its own channel's feed.

### macOS signing and notarization (optional)

Without an Apple Developer account the release workflow still produces a working, **unsigned** macOS installer and logs a warning; users then need the Gatekeeper steps under [Installing on macOS](#installing-on-macos). To ship a signed and notarized build, add these repository secrets (Settings → Secrets and variables → Actions). The workflow signs only when all seven are set:

| Secret | What it is |
|---|---|
| `APPLE_CERT_P12_BASE64` | One `.p12` exported from Keychain Access holding **both** your *Developer ID Application* and *Developer ID Installer* certificates and their private keys, base64-encoded: `base64 -i certs.p12 \| pbcopy` |
| `APPLE_CERT_PASSWORD` | The password you set when exporting the `.p12` |
| `APPLE_SIGN_APP_IDENTITY` | e.g. `Developer ID Application: Your Name (TEAMID)` (as shown by `security find-identity -v`) |
| `APPLE_SIGN_INSTALL_IDENTITY` | e.g. `Developer ID Installer: Your Name (TEAMID)` |
| `APPLE_NOTARY_APPLE_ID` | The Apple ID email used for notarization |
| `APPLE_NOTARY_PASSWORD` | An app-specific password for that Apple ID (appleid.apple.com → Sign-In and Security → App-Specific Passwords) |
| `APPLE_TEAM_ID` | Your 10-character Team ID (developer.apple.com → Membership) |

The workflow imports the certificate into a temporary keychain, stores the notary credentials with `xcrun notarytool store-credentials`, and passes `--signAppIdentity`, `--signInstallIdentity`, `--signEntitlements` and `--notaryProfile` to `vpk pack`, which signs with the hardened runtime, notarizes and staples. Notarization usually takes a few minutes but can take longer.

## Installing

Download from the [latest release](https://github.com/wiselead-oss/pc-cleaner/releases/latest).

**Windows:** run `PCCleaner-win-Setup.exe`.

### Installing on macOS

Download `PCCleaner-osx-Setup.pkg` and open it. The app is a universal build, so the same installer works on Apple Silicon and Intel Macs running macOS 14 (Sonoma) or later. (`PCCleaner-osx-Portable.zip` is the same app without an installer. Unzip it and drag **PC Cleaner** into Applications.)

Until releases are signed and notarized (see above), macOS blocks the first open with a message like *"PCCleaner-osx-Setup.pkg" cannot be opened because Apple cannot check it for malicious software*, or *"…" Not Opened*. To get past it once:

1. Click **Done** (or **OK**) on the warning. Don't click Move to Trash.
2. Open **System Settings → Privacy & Security** and scroll down to **Security**. You'll see a line saying the installer (or app) was blocked. Click **Open Anyway** and confirm with your password or Touch ID.
3. The installer then runs normally. If macOS also blocks **PC Cleaner** the first time you launch it, repeat step 2 for the app.

On older macOS versions (14 Sonoma and earlier) you can instead **Control-click (right-click) the `.pkg` or app → Open → Open**.

If macOS says the app *"is damaged and can't be opened"* (this can happen with the portable zip), clear the download quarantine flag in Terminal and open it again:

```bash
xattr -dr com.apple.quarantine "/Applications/PC Cleaner.app"
```

After installing, grant **Full Disk Access** (System Settings → Privacy & Security → Full Disk Access → add PC Cleaner) so it can scan other apps' caches and your folders.

## macOS: App Sandbox is disabled on purpose

`Platforms/MacCatalyst/Entitlements.plist` ships with `com.apple.security.app-sandbox = false`. A sandboxed app cannot read another app's caches, manage LaunchAgents outside its own container, or browse arbitrary user folders — all of which this app needs to do. This is standard for the category (CleanMyMac, DaisyDisk, and OnyX are all unsandboxed) and means the app must be **notarized and distributed directly**, not through the Mac App Store, which requires sandboxing.

## Linux packaging: AppImage, unsandboxed, self-updating

Linux ships as a single **AppImage** (`PCCleaner.AppImage` on each GitHub Release), built by Velopack from a self-contained `linux-x64` publish of the Avalonia app. Nothing else — no Flatpak, no `.deb`/`.rpm`. The decision, and why:

- **Not Flatpak.** A Flatpak is sandboxed, and this app's whole job is the opposite of what a sandbox allows: it reads other applications' caches (`~/.cache/*`, browser profiles, package-manager caches) and creates and edits autostart entries (`~/.config/autostart`, systemd user units). Making that work would mean `--filesystem=home` plus host access broad enough that the sandbox protects nothing, and some of it (managing the host's systemd user units) still wouldn't work from inside one. This is the same call the macOS build makes for the same reasons — it ships with the App Sandbox off (see above). A sandbox that has to be switched off to do the job is worse than none, because it implies protection that isn't there.
- **Not `.deb`/`.rpm` (for now).** A distro package hands updates to the system package manager, so the in-app "Check for updates" would have nothing honest to offer. Not ruled out, but it would need its own repository and signing, and the About screen already copes with it (below).
- **AppImage** is the closest match to how Windows and macOS ship: one file, no install step, no root, and Velopack supports it — so the same `UpdateService` design (Velopack's `UpdateManager` over `GithubSource`) updates it in place, reading `releases.linux.json` from the same release the other platforms use.

**Install:** download `PCCleaner.AppImage` from the latest release, `chmod +x PCCleaner.AppImage`, and run it. Keep it somewhere your user can write to (e.g. `~/Applications`): an update replaces the file in place. The AppImage needs FUSE to mount itself (`libfuse2` on Ubuntu 22.04+); without it, run it with `--appimage-extract-and-run`.

**Menu entry:** the AppImage carries its own `.desktop` entry and icon, which [Gear Lever](https://github.com/mijorus/gearlever) or [AppImageLauncher](https://github.com/TheAssassin/AppImageLauncher) will install for you. To do it by hand, from a checkout:

```bash
install -Dm644 packaging/linux/icons/hicolor/256x256/apps/pccleaner.png ~/.local/share/icons/hicolor/256x256/apps/pccleaner.png
install -Dm644 packaging/linux/icons/hicolor/scalable/apps/pccleaner.svg ~/.local/share/icons/hicolor/scalable/apps/pccleaner.svg
sed "s|^Exec=.*|Exec=$HOME/Applications/PCCleaner.AppImage|" packaging/linux/pccleaner.desktop > ~/.local/share/applications/pccleaner.desktop
```

The icon is `src/PcCleaner.App/Resources/AppIcon/appicon.svg` with `appiconfg.svg` composited on top, the same artwork MAUI renders for Windows and macOS.

**Updates and the About screen.** Velopack only treats a copy as installed when it's running from inside a vpk-built AppImage and `$APPIMAGE` (set by the AppImage runtime) points at the file to replace. Only then does the About screen show "Check for updates". Every other copy — `dotnet run`, a source build, a package someone else made, an AppImage not built by this repo's release workflow — hides the button and says plainly that updates aren't available for this install and why, instead of offering one that can't work.

## Known gaps / next steps

- **Linux is a scaffold.** The Avalonia front end builds, ships as an AppImage and updates itself, but its junk scanner, trash, startup manager, folder picker and theme services are still stubs that touch nothing (#10-#13), and only the Junk Files and About screens are ported (#9).
- **Folder picking** uses the native OS browser (`Windows.Storage.Pickers.FolderPicker` on Windows, `UIDocumentPickerViewController` on macOS) via `IFolderPickerService`, with the typed-path entry kept as a fallback. `CommunityToolkit.Maui`'s `FolderPicker` is still unusable here — its current version requires a newer `Microsoft.Maui.Controls` than this SDK's `maui-windows`/`maccatalyst` workload ships, causing a version-downgrade conflict — hence the hand-rolled platform services.
- **Recycle Bin / Trash failures on system-owned junk paths** (e.g. `C:\Windows\SoftwareDistribution\Download`, `/Library/Caches`) are expected without elevated privileges — the cleaner reports these as partial failures rather than crashing, but doesn't yet prompt for elevation.
- Startup item disabling for **system-scope** entries (`HKLM` Run keys on Windows, `/Library/LaunchAgents`, `/Library/LaunchDaemons` on macOS) requires admin/root and will throw; the UI surfaces the error but doesn't yet offer an elevation prompt.

## Contributing

Bug reports, ideas and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers building, testing and what a good pull request looks like. Security problems go through [SECURITY.md](SECURITY.md), not a public issue.

## License

PC Cleaner is open source under the [Apache License 2.0](LICENSE), copyright 2026 WiseLead Solutions. Third-party software and fonts are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The name "PC Cleaner", the WiseLead name and the icon are trademarks and are not covered by the code licence; see [TRADEMARKS.md](TRADEMARKS.md).
