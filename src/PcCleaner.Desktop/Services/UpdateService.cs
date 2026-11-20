using System.Reflection;
using PcCleaner.Presentation.Services;
using Velopack;
using Velopack.Sources;

namespace PcCleaner.Desktop.Services;

/// <summary>
/// Velopack over GitHub Releases, the same updater the MAUI app uses. On Linux it updates the AppImage in place.
/// </summary>
/// <remarks>
/// <para>
/// Linux ships one self-updating format, the AppImage the release workflow builds with <c>vpk pack</c> (see README,
/// "Linux packaging"). Velopack recognises it from two things together: the process is running out of a mounted
/// AppImage (<c>…/usr/bin/</c> beside vpk's <c>UpdateNix</c> and <c>sq.version</c>), and <c>$APPIMAGE</c> — set by
/// the AppImage runtime — names the .AppImage file it will replace. Every other way of running this front end
/// (<c>dotnet run</c>, a source build, a copy repackaged by a distro or by hand) is not something this app can
/// update, and <see cref="NotInstalledMessage"/> says which case applies instead of offering a button that can't
/// work.
/// </para>
/// <para>
/// No locator workaround is needed here, unlike the Mac Catalyst build (<c>VelopackLocatorProvider</c> in
/// PcCleaner.App): this is plain net10.0, so on Linux <c>OperatingSystem.IsLinux()</c> is true and Velopack picks
/// its Linux locator itself. That locator doesn't throw outside an AppImage — it reports not-installed. Velopack
/// throws only on an OS it has no locator for (e.g. FreeBSD), which is caught and treated the same way.
/// </para>
/// <para>
/// The update channel isn't set: <see cref="UpdateManager"/> reads it from the installed package's manifest, which
/// the release workflow packs as <c>linux</c>, so it checks <c>releases.linux.json</c> and never Windows' or macOS'.
/// </para>
/// </remarks>
public sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/dara-oladapo/pc-cleaner";

    /// <summary>Set by the AppImage runtime to the path of the .AppImage file; unset for any other launch.</summary>
    public const string AppImageVariable = "APPIMAGE";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
        }
        catch (Exception)
        {
            _manager = null;
        }

        NotInstalledMessage = DescribeNotInstalled(Environment.GetEnvironmentVariable(AppImageVariable));
    }

    public bool IsInstalled => _manager?.IsInstalled ?? false;

    public string NotInstalledMessage { get; }

    public string CurrentVersionText => IsInstalled
        ? _manager!.CurrentVersion?.ToString() ?? "unknown"
        : $"{AssemblyVersion()} (updates not available for this install)";

    public string? PendingVersionText => _pendingUpdate?.TargetFullRelease.Version.ToString();

    public async Task<UpdateCheckStatus> CheckForUpdatesAsync()
    {
        if (!IsInstalled)
        {
            return UpdateCheckStatus.NotInstalled;
        }

        try
        {
            _pendingUpdate = await _manager!.CheckForUpdatesAsync();
            return _pendingUpdate is null ? UpdateCheckStatus.UpToDate : UpdateCheckStatus.UpdateAvailable;
        }
        catch (Exception)
        {
            return UpdateCheckStatus.CheckFailed;
        }
    }

    public async Task DownloadAndRestartAsync(Action<int>? onProgress = null)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException($"Call {nameof(CheckForUpdatesAsync)} first and confirm an update is available.");
        }

        // On Linux, applying replaces the .AppImage file at $APPIMAGE. If that file isn't writable by this user
        // (say it was copied into /opt as root), Velopack throws and the About screen shows the error.
        await _manager!.DownloadUpdatesAsync(_pendingUpdate, onProgress);
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }

    /// <summary>Why a copy that isn't Velopack-installed can't update, in words a user can act on.</summary>
    /// <param name="appImagePath">The value of <c>$APPIMAGE</c>, or null when it's unset.</param>
    public static string DescribeNotInstalled(string? appImagePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return "Updates aren't available for this install: this is a development build of the Linux front end, " +
                   "which only updates itself when run as the AppImage from GitHub Releases.";
        }

        if (string.IsNullOrEmpty(appImagePath))
        {
            return "Updates aren't available for this install. PC Cleaner only updates itself when it's run from the " +
                   "AppImage on GitHub Releases, and this copy wasn't started from one (a source build, or a package " +
                   "made by someone else). Update it the same way you installed it.";
        }

        return "Updates aren't available for this install. This AppImage wasn't built by PC Cleaner's release " +
               "pipeline, so it can't update itself — download the latest PCCleaner.AppImage from GitHub Releases.";
    }

    private static string AssemblyVersion()
    {
        string? version = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<commit>" to the informational version; the number alone is what a user compares.
        return version?.Split('+')[0] ?? "dev build";
    }
}
