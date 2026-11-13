using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Scanning;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// The "which folders do we scan" half of any tool that walks user folders — the duplicate finder and the
/// large file finder need exactly the same list, defaults, and add/remove behaviour.
/// </summary>
public abstract partial class ScanRootsViewModel : ObservableObject
{
    private readonly IFolderPickerService _folderPicker;
    private readonly ScanVolumeSettings _volumeSettings;

    public ObservableCollection<string> RootPaths { get; } = [];

    [ObservableProperty]
    public partial string NewFolderPath { get; set; } = string.Empty;

    protected ScanRootsViewModel(IFolderPickerService folderPicker, ScanVolumeSettings volumeSettings)
    {
        _folderPicker = folderPicker;
        _volumeSettings = volumeSettings;

        foreach (string folder in DefaultFolders())
        {
            RootPaths.Add(folder);
        }
    }

    /// <summary>Set by the derived tool so a rejected folder can explain itself in that tool's status line.</summary>
    protected abstract void ReportRootPathProblem(string message);

    /// <summary>
    /// The folders this scan will really walk, after the "Volumes to scan" setting is applied: folders on
    /// unselected or missing volumes drop out, and each selected non-system volume is added whole.
    /// </summary>
    protected ScanPlan PlanScan() => _volumeSettings.PlanScan(RootPaths.ToList());

    /// <summary>Why a plan has nothing to walk, phrased for the tool's status line.</summary>
    protected static string NothingToScanText(ScanPlan plan) =>
        plan.Skipped.Count == 0 && plan.UnavailableVolumes.Count == 0
            ? "Add a folder, or select a volume in Settings."
            : $"Nothing to scan. {SkippedText(plan)}";

    /// <summary>
    /// What the scan did not look at, or empty. Appended to the result so "nothing found" is never mistaken
    /// for "looked everywhere".
    /// </summary>
    protected static string SkippedText(ScanPlan plan)
    {
        var parts = new List<string>();

        if (plan.UnavailableVolumes.Count > 0)
        {
            parts.Add($"Not connected, so skipped: {string.Join(", ", plan.UnavailableVolumes)}.");
        }

        if (plan.Skipped.Count == 1)
        {
            parts.Add($"Skipped {plan.Skipped[0].Path}: {plan.Skipped[0].Reason}");
        }
        else if (plan.Skipped.Count > 1)
        {
            parts.Add($"Skipped {plan.Skipped.Count} folders on volumes that are not selected or not connected.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Appends <see cref="SkippedText"/> to a result sentence when there is anything to say.</summary>
    protected static string WithSkippedNote(string status, ScanPlan plan)
    {
        string note = SkippedText(plan);
        return note.Length == 0 ? status : $"{status} {note}";
    }

    private static IEnumerable<string> DefaultFolders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
        ];

        return candidates.Where(Directory.Exists).Distinct();
    }

    /// <summary>Opens the OS folder browser. The typed entry below it still works if the picker is unavailable.</summary>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? picked = await _folderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            AddPath(picked);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        string path = NewFolderPath.Trim();
        if (path.Length == 0)
        {
            return;
        }

        if (AddPath(path))
        {
            NewFolderPath = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveFolder(string path) => RootPaths.Remove(path);

    private bool AddPath(string path)
    {
        if (!Directory.Exists(path))
        {
            ReportRootPathProblem($"'{path}' isn't a folder that exists.");
            return false;
        }

        if (!RootPaths.Contains(path))
        {
            RootPaths.Add(path);
        }

        return true;
    }
}
