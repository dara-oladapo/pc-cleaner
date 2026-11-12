using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Stub until #12 (XDG autostart + systemd user units). Lists nothing and changes nothing.</summary>
public sealed class LinuxStartupManager : IStartupItemManager
{
    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StartupItem>>([]);

    // Unreachable while the list above is empty; throws rather than silently "succeeding" if that changes.
    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Startup items are not implemented on Linux yet (see #12).");
}
