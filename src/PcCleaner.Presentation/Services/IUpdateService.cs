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

    string CurrentVersionText { get; }

    string? PendingVersionText { get; }

    Task<UpdateCheckStatus> CheckForUpdatesAsync();

    Task DownloadAndRestartAsync(Action<int>? onProgress = null);
}
