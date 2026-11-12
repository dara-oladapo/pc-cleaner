namespace PcCleaner.Presentation.Services;

public enum UpdateCheckStatus
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    CheckFailed,
}

/// <summary>Checks for a newer release, downloads it, and restarts into it.</summary>
/// <remarks>
/// An interface so <c>AboutViewModel</c> doesn't depend on the installer/updater package the app ships with.
/// </remarks>
public interface IUpdateService
{
    /// <summary>False when running a plain dev build (not an installed copy) — nothing to update.</summary>
    bool IsInstalled { get; }

    /// <summary>
    /// What the About screen says when <see cref="IsInstalled"/> is false: why this copy can't update itself.
    /// </summary>
    /// <remarks>
    /// The default fits the Velopack Setup installers, where "not installed" only ever means a dev build. A front end
    /// with more than one way to be not-installed (Linux: a source build, or an AppImage that isn't ours) overrides
    /// it so the screen states the real reason instead of telling a user they're running a dev build.
    /// </remarks>
    string NotInstalledMessage =>
        "This is a dev build, not an installed copy — updates only work once installed via the Setup installer.";

    string CurrentVersionText { get; }

    string? PendingVersionText { get; }

    Task<UpdateCheckStatus> CheckForUpdatesAsync();

    Task DownloadAndRestartAsync(Action<int>? onProgress = null);
}
