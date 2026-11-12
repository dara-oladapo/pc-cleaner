using System.Reflection;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Services;

/// <summary>
/// <see cref="IUpdateService"/> for a build that has no updater. Says so, and never offers an update.
/// </summary>
/// <remarks>
/// The MAUI app's updater is Velopack over GitHub Releases; Linux packaging and how it updates (distro package,
/// Flatpak, AppImage, or Velopack) is #14. Until then this reports "not installed" so no download is ever
/// offered, and the version text carries the plain statement that updates aren't supported here.
/// </remarks>
public sealed class UnsupportedUpdateService : IUpdateService
{
    public bool IsInstalled => false;

    public string CurrentVersionText { get; } =
        $"{typeof(UnsupportedUpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev build"} (updates are not supported on this build)";

    public string? PendingVersionText => null;

    public Task<UpdateCheckStatus> CheckForUpdatesAsync() => Task.FromResult(UpdateCheckStatus.NotInstalled);

    public Task DownloadAndRestartAsync(Action<int>? onProgress = null) =>
        throw new NotSupportedException("Updates are not supported on this build (see #14).");
}
