using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;

namespace PcCleaner.App.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updates;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool UpdateReady { get; set; }

    [ObservableProperty]
    public partial string VersionText { get; set; } = "dev build";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Tap Check for Updates to look for a newer release.";

    public AboutViewModel(UpdateService updates)
    {
        _updates = updates;
        VersionText = _updates.CurrentVersionText;
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
            UpdateCheckStatus.NotInstalled =>
                "This is a dev build, not an installed copy — updates only work once installed via the Setup installer.",
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
