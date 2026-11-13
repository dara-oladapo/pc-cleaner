using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>One volume row: a checkbox on the Settings page, a space readout on the dashboard.</summary>
public sealed partial class VolumeChoiceViewModel : ObservableObject
{
    private readonly Action<VolumeChoiceViewModel, bool>? _onSelectionChanged;

    public VolumeChoiceViewModel(VolumeChoice choice, Action<VolumeChoiceViewModel, bool>? onSelectionChanged = null)
    {
        RootPath = choice.RootPath;
        IsAvailable = choice.IsAvailable;

        ScanVolume? volume = choice.Volume;
        DisplayName = volume?.DisplayName ?? choice.RootPath;
        KindText = volume is null ? "Unavailable" : KindLabel(volume.Kind);
        UsedShare = volume?.UsedShare ?? 0;
        SpaceText = volume is null
            ? "Not connected. Skipped until it is back."
            : $"{ByteSizeFormatter.Format(volume.FreeBytes)} free of {ByteSizeFormatter.Format(volume.TotalBytes)}";

        // Set before the callback is wired, so building the row doesn't write the setting back.
        IsSelected = choice.IsSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    public string RootPath { get; }

    public string DisplayName { get; }

    public string KindText { get; }

    public string SpaceText { get; }

    /// <summary>0..1 of the volume in use, for the row's meter.</summary>
    public double UsedShare { get; }

    public bool IsAvailable { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke(this, value);

    private static string KindLabel(ScanVolumeKind kind) => kind switch
    {
        ScanVolumeKind.System => "System",
        ScanVolumeKind.Fixed => "Internal",
        ScanVolumeKind.Removable => "Removable",
        ScanVolumeKind.External => "External",
        ScanVolumeKind.Virtual => "Virtual disk",
        _ => kind.ToString(),
    };
}
