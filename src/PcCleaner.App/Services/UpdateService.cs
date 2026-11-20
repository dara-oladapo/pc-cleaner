using PcCleaner.Presentation.Services;
using Velopack;
using Velopack.Sources;

namespace PcCleaner.App.Services;

/// <summary>Wraps Velopack's UpdateManager: checks GitHub Releases for a newer build, downloads it, and restarts into it.</summary>
public sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/dara-oladapo/pc-cleaner";

    // Null where Velopack has no usable locator (a Mac Catalyst build that vpk didn't pack): constructing an
    // UpdateManager there throws, and there's nothing to update anyway.
    private readonly UpdateManager? _manager = VelopackLocatorProvider.IsSupported
        ? new UpdateManager(
            new GithubSource(RepoUrl, accessToken: null, prerelease: false),
            options: null,
            locator: VelopackLocatorProvider.Locator)
        : null;
    private UpdateInfo? _pendingUpdate;

    /// <summary>False when running a plain dev build (not launched via a Velopack-installed copy) — nothing to update.</summary>
    public bool IsInstalled => _manager?.IsInstalled ?? false;

    public string CurrentVersionText => IsInstalled
        ? _manager!.CurrentVersion?.ToString() ?? "unknown"
        : "dev build";

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

        await _manager!.DownloadUpdatesAsync(_pendingUpdate, onProgress);
        _manager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
