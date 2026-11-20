using System.Text.Json;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Presentation.Services;

/// <summary>
/// The "Volumes to scan" setting: which volumes the large-file and duplicate scans may walk. Shared by the
/// Settings page, both scan tools, and the dashboard's per-volume overview.
/// </summary>
/// <remarks>
/// Stored as a JSON array of mount roots. "Never set" (no key) and "set to nothing" (<c>[]</c>) are
/// different on purpose: the first means "use the default, the system volume", the second means the user
/// turned everything off. A root that isn't mounted today stays in the list, so a disk that is unplugged
/// between runs is still selected when it comes back.
/// </remarks>
public sealed class ScanVolumeSettings
{
    public const string SettingsKey = "scan_volumes";

    private readonly ISettingsStore _store;
    private readonly IVolumeCatalog _catalog;

    public ScanVolumeSettings(ISettingsStore store, IVolumeCatalog catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    /// <summary>Raised after the selection is saved, so the dashboard can redraw its overview.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the mounted volumes now. Not cached: a disk plugged in or pulled out shows up on the next read.</summary>
    public VolumeSnapshot ReadVolumes() => _catalog.Read();

    public IReadOnlyList<string> GetSelectedRoots(VolumeSnapshot snapshot) =>
        Load() ?? ScanVolumePlanner.DefaultSelection(snapshot);

    public IReadOnlyList<VolumeChoice> GetChoices(VolumeSnapshot snapshot) =>
        ScanVolumePlanner.ListChoices(snapshot, GetSelectedRoots(snapshot).ToList());

    /// <summary>Applies the setting to a tool's folder list. Reads the volumes fresh, so a missing disk is skipped.</summary>
    public ScanPlan PlanScan(IReadOnlyList<string> folderRoots)
    {
        VolumeSnapshot snapshot = ReadVolumes();
        return ScanVolumePlanner.Plan(folderRoots, snapshot, GetSelectedRoots(snapshot).ToList());
    }

    public void SetSelected(string rootPath, bool selected)
    {
        VolumeSnapshot snapshot = ReadVolumes();
        StringComparison comparison = snapshot.PathComparison;
        var roots = GetSelectedRoots(snapshot).ToList();

        roots.RemoveAll(r => string.Equals(r, rootPath, comparison));
        if (selected)
        {
            roots.Add(rootPath);
        }

        _store.Set(SettingsKey, JsonSerializer.Serialize(roots));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<string>? Load()
    {
        string? json = _store.Get(SettingsKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList();
        }
        catch (JsonException)
        {
            // A corrupt value falls back to the default rather than stopping the app or a scan.
            return null;
        }
    }
}
