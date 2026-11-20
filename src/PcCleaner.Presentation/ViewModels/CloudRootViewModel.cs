using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>One cloud folder (sync root) on the Cloud drives page: what is downloaded and what can be freed.</summary>
public sealed partial class CloudRootViewModel : ObservableObject
{
    public CloudRootViewModel(CloudRootScan scan)
    {
        Scan = scan;
        CloudSyncRoot root = scan.Root;

        foreach (CloudFileVerdict verdict in scan.LargestFiles)
        {
            var file = new CloudFileViewModel(verdict, root.Path, root.CanFreeUp);
            file.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CloudFileViewModel.IsSelected))
                {
                    NotifySelectionChanged();
                }
            };
            LargestFiles.Add(file);
        }

        // Selected by default only when there is something safe to free, this app can free it, and the
        // provider is one we recognise. An unrecognised provider is listed, but the user has to opt in.
        IsSelected = CanSelect && root.Provider != CloudProvider.Other;
    }

    public event EventHandler? SelectionChanged;

    public CloudRootScan Scan { get; }

    public CloudSyncRoot Root => Scan.Root;

    public ObservableCollection<CloudFileViewModel> LargestFiles { get; } = [];

    public string Title => Root.DisplayName;

    public string ProviderText => CloudProviderClassifier.Label(Root.Provider);

    public string Path => Root.Path;

    public string HydratedText => $"{ByteSizeFormatter.Format(Scan.HydratedBytes)} downloaded · {Scan.HydratedCount:N0} files";

    public string EligibleSizeText => ByteSizeFormatter.Format(Scan.EligibleBytes);

    public string EligibleText => Root.CanFreeUp
        ? $"{Scan.EligibleCount:N0} files can be freed"
        : $"{Scan.EligibleCount:N0} files could be freed by the provider";

    /// <summary>What stays on the device and why, biggest reason first.</summary>
    public string KeptText
    {
        get
        {
            var parts = Scan.IneligibleBytesByReason
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => $"{ByteSizeFormatter.Format(kv.Value)} {CloudEligibilityRules.Describe(kv.Key).ToLowerInvariant()}")
                .ToList();
            return parts.Count == 0 ? string.Empty : "Kept: " + string.Join(", ", parts);
        }
    }

    public bool HasKeptText => KeptText.Length > 0;

    public bool CanSelect => Root.CanFreeUp && Scan.EligibleCount > 0;

    public bool IsReadOnly => !Root.CanFreeUp;

    public string ReadOnlyText => Root.ReadOnlyReason ?? string.Empty;

    public bool HasLargestFiles => LargestFiles.Count > 0;

    /// <summary>0..1 share of the largest root on screen, for the row's meter.</summary>
    [ObservableProperty]
    public partial double ShareOfMax { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string ExpandText => IsExpanded ? "Hide largest files" : "Show largest files";

    /// <summary>Bytes that freeing this root would release, given what is ticked.</summary>
    public long SelectedBytes => IsSelected
        ? Scan.EligibleBytes - LargestFiles.Where(f => f.IsEligible && !f.IsSelected).Sum(f => f.File.AllocatedBytes)
        : 0;

    /// <summary>The files to release: every eligible file, minus any the user unticked in the expanded list.</summary>
    public IReadOnlyList<CloudReleaseRequest> SelectedRequests()
    {
        if (!IsSelected || !Root.CanFreeUp)
        {
            return [];
        }

        var kept = LargestFiles.Where(f => !f.IsSelected).Select(f => f.File.Path).ToHashSet(StringComparer.Ordinal);
        return Scan.Eligible
            .Where(f => !kept.Contains(f.Path))
            .Select(f => new CloudReleaseRequest(Root, f.Path))
            .ToList();
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandText));

    partial void OnIsSelectedChanged(bool value)
    {
        // A root that cannot be freed, or has nothing eligible, can never be selected.
        if (value && !CanSelect)
        {
            IsSelected = false;
            return;
        }

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
