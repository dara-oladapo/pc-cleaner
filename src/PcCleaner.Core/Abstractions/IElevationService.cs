using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>
/// Platform-specific: runs one admin/root-only operation with elevated rights, without running the whole app elevated.
/// </summary>
/// <remarks>
/// Each method maps to a single, fixed operation the app supports. Nothing here accepts a free-form command: the
/// platform implementation validates every target against an allowlist before asking the OS for elevation (and, on
/// Windows, the elevated helper validates it again on its own side), so an elevation prompt can only ever approve
/// one of these operations on one of the expected locations.
/// </remarks>
public interface IElevationService
{
    /// <summary>True when <paramref name="item"/> is a system-scope entry this service knows how to toggle elevated.</summary>
    bool CanElevate(StartupItem item);

    /// <summary>Enables or disables a system-scope startup entry with elevated rights. Prompts the OS for consent.</summary>
    Task<ElevationResult> SetStartupItemEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Of the system-owned junk roots this service can clean elevated, returns those that contain at least one of
    /// <paramref name="failedPaths"/>. Empty when elevation would not help (e.g. a file locked by a running app).
    /// </summary>
    IReadOnlyList<string> GetElevatableJunkRoots(IEnumerable<string> failedPaths);

    /// <summary>Deletes the contents of the given system junk roots with elevated rights. Prompts the OS for consent.</summary>
    Task<ElevationResult> CleanJunkRootsAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default);
}
