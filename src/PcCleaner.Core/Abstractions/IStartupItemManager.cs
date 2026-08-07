using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Platform-specific: reads/toggles OS-level autostart entries (registry, plists, scheduled tasks).</summary>
public interface IStartupItemManager
{
    Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default);

    Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default);
}
