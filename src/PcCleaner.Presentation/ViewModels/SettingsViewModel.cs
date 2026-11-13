using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>The Settings page: which volumes the large-file and duplicate scans walk, and developer mode.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ScanVolumeSettings _volumeSettings;
    private readonly DeveloperModeSettings _developerMode;
    private readonly IDialogService _dialogs;

    public ObservableCollection<VolumeChoiceViewModel> Volumes { get; } = [];

    [ObservableProperty]
    public partial string VolumesStatusText { get; set; } = string.Empty;

    public SettingsViewModel(ScanVolumeSettings volumeSettings, DeveloperModeSettings developerMode, IDialogService dialogs)
    {
        _volumeSettings = volumeSettings;
        _developerMode = developerMode;
        _dialogs = dialogs;
        _developerMode.Changed += (_, _) => OnPropertyChanged(nameof(DeveloperMode));
        RefreshVolumes();
    }

    /// <summary>Two-way bound to the toggle. Only ever changed by the user.</summary>
    public bool DeveloperMode
    {
        get => _developerMode.IsEnabled;
        set => _developerMode.IsEnabled = value;
    }

    /// <summary>
    /// Once per install, on a machine with git or docker on disk (looked up, never run), asks whether to turn
    /// developer mode on. Declining is remembered the same as accepting; it is never turned on without a yes.
    /// </summary>
    public Task SuggestDeveloperModeOnceAsync() => SuggestDeveloperModeOnceAsync(ToolLocator.IsInstalled);

    public async Task SuggestDeveloperModeOnceAsync(Func<string, bool> isInstalled)
    {
        if (!DeveloperModeAdvisor.ShouldSuggest(_developerMode.IsEnabled, _developerMode.SuggestionShown, isInstalled))
        {
            return;
        }

        _developerMode.SuggestionShown = true;
        bool accepted = await _dialogs.ConfirmAsync(
            title: "Turn on developer mode?",
            message: "Git or Docker is installed. Developer mode adds a Developer page for Docker build cache, stale git "
                + "worktrees and old build output, and includes developer tool caches in Junk Files. It stays off unless "
                + "you turn it on, and you can change it any time in Settings.",
            acceptText: "Turn on",
            cancelText: "Not now",
            isDestructive: false,
            details: null);

        if (accepted)
        {
            DeveloperMode = true;
        }
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
