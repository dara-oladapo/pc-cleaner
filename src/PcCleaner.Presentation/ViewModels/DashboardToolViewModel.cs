using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// One tool as the dashboard sees it: whether it takes part in a run, what it found, and its tickable parts.
/// </summary>
/// <remarks>
/// A thin adapter over the tool's own view model, built from delegates. That keeps the dashboard run
/// (<see cref="DashboardRunner"/>) independent of what each tool is, and testable without any of them.
/// </remarks>
public sealed partial class DashboardToolViewModel : ObservableObject
{
    /// <summary>Parts listed before the row says "+ N more". The tool's own page shows everything.</summary>
    public const int MaxParts = 8;

    /// <summary>The Shell route the row's "Open" link goes to.</summary>
    public required string Route { get; init; }

    public required string Title { get; init; }

    /// <summary>What a clean does to this tool's items, in a few words: "Deleted permanently".</summary>
    public required string Caption { get; init; }

    /// <summary>False for a tool that only reports (Startup). It is scanned but never cleaned.</summary>
    public bool CanClean { get; init; } = true;

    public required Func<Task> Scan { get; init; }

    /// <summary>Cleans what is ticked. The argument is whether the tool should ask first; the dashboard passes false.</summary>
    public Func<bool, Task> Clean { get; init; } = _ => Task.CompletedTask;

    public Action Cancel { get; init; } = () => { };

    public Func<long> SelectedBytes { get; init; } = () => 0;

    /// <summary>How many things are ticked. Separate from the bytes because some actions have no known size.</summary>
    public Func<int> SelectedCount { get; init; } = () => 0;

    /// <summary>The figure on the row: what is ticked, or what was found.</summary>
    public Func<string> Summary { get; init; } = () => string.Empty;

    /// <summary>The tool's own account of its last clean, for the run report.</summary>
    public Func<string> Outcome { get; init; } = () => string.Empty;

    /// <summary>The first <see cref="MaxParts"/> parts, and how many there are in all.</summary>
    public Func<(IReadOnlyList<DashboardPartViewModel> Parts, int Total)> BuildParts { get; init; } = () => ([], 0);

    public ObservableCollection<DashboardPartViewModel> Parts { get; } = [];

    /// <summary>Whether a dashboard run scans and cleans this tool.</summary>
    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Set as the current run finishes this tool, for the progress markers.</summary>
    [ObservableProperty]
    public partial bool IsStepDone { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "Not scanned yet";

    [ObservableProperty]
    public partial bool HasParts { get; set; }

    [ObservableProperty]
    public partial string MoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMore { get; set; }

    public string ExpandText => IsExpanded ? "Hide" : "Show";

    public bool ShowParts => IsExpanded && HasParts;

    public bool ShowMore => IsExpanded && HasMore;

    /// <summary>Raised when the include tick changes, so the dashboard can redo its totals.</summary>
    public event EventHandler? IncludedChanged;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Rebuilds the parts from the tool. Called after a scan or a clean, when the tool's lists have changed.</summary>
    public void Rebuild()
    {
        (IReadOnlyList<DashboardPartViewModel> parts, int total) = BuildParts();

        Parts.Clear();
        foreach (DashboardPartViewModel part in parts.Take(MaxParts))
        {
            Parts.Add(part);
        }

        int more = total - Parts.Count;
        HasParts = Parts.Count > 0;
        HasMore = more > 0;
        MoreText = more > 0 ? $"+ {more:N0} more. Open {Title} to see them all." : string.Empty;
        Refresh();
    }

    /// <summary>Re-reads the figure and every tick. Called when a selection changes, here or on the tool's page.</summary>
    public void Refresh()
    {
        SummaryText = Summary();
        foreach (DashboardPartViewModel part in Parts)
        {
            part.Refresh();
        }
    }

    partial void OnIsIncludedChanged(bool value) => IncludedChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandText));
        OnPropertyChanged(nameof(ShowParts));
        OnPropertyChanged(nameof(ShowMore));
    }

    partial void OnHasPartsChanged(bool value) => OnPropertyChanged(nameof(ShowParts));

    partial void OnHasMoreChanged(bool value) => OnPropertyChanged(nameof(ShowMore));
}
