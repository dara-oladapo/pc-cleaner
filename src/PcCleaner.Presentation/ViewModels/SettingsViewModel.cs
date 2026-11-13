using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>The Settings page. Today it holds one setting: which volumes the large-file and duplicate scans walk.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ScanVolumeSettings _volumeSettings;

    public ObservableCollection<VolumeChoiceViewModel> Volumes { get; } = [];

    [ObservableProperty]
    public partial string VolumesStatusText { get; set; } = string.Empty;

    public SettingsViewModel(ScanVolumeSettings volumeSettings)
    {
        _volumeSettings = volumeSettings;
        RefreshVolumes();
    }

    /// <summary>Re-reads what is mounted. Also run each time the page appears, so a disk plugged in since shows up.</summary>
    [RelayCommand]
    private void RefreshVolumes()
    {
        var snapshot = _volumeSettings.ReadVolumes();

        Volumes.Clear();
        foreach (var choice in _volumeSettings.GetChoices(snapshot))
        {
            Volumes.Add(new VolumeChoiceViewModel(choice, OnVolumeToggled));
        }

        UpdateStatus();
    }

    private void OnVolumeToggled(VolumeChoiceViewModel volume, bool selected)
    {
        _volumeSettings.SetSelected(volume.RootPath, selected);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int selected = Volumes.Count(v => v.IsSelected);
        int missing = Volumes.Count(v => v.IsSelected && !v.IsAvailable);

        VolumesStatusText = selected == 0
            ? "No volume selected. Large-file and duplicate scans will have nothing to look at."
            : missing > 0
                ? $"{selected} selected, {missing} not connected right now."
                : $"{selected} selected.";
    }
}
