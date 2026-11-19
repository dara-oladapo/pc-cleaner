using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IUpdateService _updates;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool UpdateReady { get; set; }

    [ObservableProperty]
    public partial string VersionText { get; set; } = "dev build";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Tap Check for Updates to look for a newer release.";

    /// <summary>
    /// False when this copy can't update itself, so a front end can hide the button rather than offer one that can
    /// only ever answer "not available".
    /// </summary>
    public bool CanSelfUpdate => _updates.IsInstalled;

    public AboutViewModel(IUpdateService updates)
    {
        _updates = updates;
        VersionText = _updates.CurrentVersionText;

        // Say it up front: "tap Check for Updates" would be a false promise on a copy that can't update.
        if (!_updates.IsInstalled)
        {
            StatusText = _updates.NotInstalledMessage;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync()
    {
        IsBusy = true;
        UpdateReady = false;
        StatusText = "Checking for updates...";

        UpdateCheckStatus status = await _updates.CheckForUpdatesAsync();

        StatusText = status switch
        {
            UpdateCheckStatus.NotInstalled => _updates.NotInstalledMessage,
            UpdateCheckStatus.UpToDate => "You're on the latest version.",
            UpdateCheckStatus.UpdateAvailable => $"Update available: v{_updates.PendingVersionText}.",
            UpdateCheckStatus.CheckFailed => "Couldn't reach the update server — check your connection and try again.",
            _ => StatusText,
        };

        UpdateReady = status == UpdateCheckStatus.UpdateAvailable;
        IsBusy = false;
        DownloadAndRestartCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheck() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAndRestartAsync()
    {
        IsBusy = true;
        StatusText = "Downloading update...";

        try
        {
            await _updates.DownloadAndRestartAsync(percent => StatusText = $"Downloading update... {percent}%");
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
            IsBusy = false;
        }
    }

    private bool CanDownload() => !IsBusy && UpdateReady;

    partial void OnIsBusyChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndRestartCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateReadyChanged(bool value) => DownloadAndRestartCommand.NotifyCanExecuteChanged();
}
