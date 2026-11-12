using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

public sealed partial class StartupManagerViewModel : ObservableObject
{
    private readonly IStartupItemManager _manager;
    private readonly IElevationService _elevation;
    private readonly IDialogService _dialogs;

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Load to see apps and services that launch automatically.";

    [ObservableProperty]
    public partial string ItemCountText { get; set; } = "0";

    public StartupManagerViewModel(IStartupItemManager manager, IElevationService elevation, IDialogService dialogs)
    {
        _manager = manager;
        _elevation = elevation;
        _dialogs = dialogs;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "Loading startup items...";
        Items.Clear();

        try
        {
            var items = await _manager.GetStartupItemsAsync();
            foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                Items.Add(new StartupItemViewModel(item));
            }

            StatusText = $"{Items.Count} startup item(s).";
            ItemCountText = Items.Count.ToString();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load startup items: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoad() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync(StartupItemViewModel item)
    {
        bool newState = !item.Model.IsEnabled;
        IsBusy = true;

        try
        {
            await _manager.SetEnabledAsync(item.Model, newState);
            StatusText = $"{item.Name} {(newState ? "enabled" : "disabled")}.";
            await LoadAsync();
        }
        catch (ElevationRequiredException)
        {
            await ToggleElevatedAsync(item, newState);
        }
        catch (Exception)
        {
            StatusText = $"Couldn't update {item.Name}. It may have been changed or removed by another app — reload and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Asks first, then retries one system-scope toggle through the OS elevation prompt (issue #3).</summary>
    private async Task ToggleElevatedAsync(StartupItemViewModel item, bool newState)
    {
        if (!_elevation.CanElevate(item.Model))
        {
            StatusText = $"Couldn't update {item.Name}: it needs administrator permission, which PC Cleaner can't request for this entry.";
            return;
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            title: "Needs administrator permission — continue?",
            message: $"{item.Name} starts for every user on this computer, so {(newState ? "enabling" : "disabling")} it needs administrator permission. Your system will ask you to approve this one change.",
            acceptText: "Continue",
            cancelText: "Cancel");

        if (!confirmed)
        {
            StatusText = $"{item.Name} was not changed.";
            return;
        }

        ElevationResult result = await _elevation.SetStartupItemEnabledAsync(item.Model, newState);
        StatusText = result.Status switch
        {
            ElevationStatus.Succeeded => $"{item.Name} {(newState ? "enabled" : "disabled")}.",
            ElevationStatus.Cancelled => $"{item.Name} was not changed — administrator permission was not given.",
            ElevationStatus.Rejected => $"Couldn't update {item.Name}: PC Cleaner can't change this entry with administrator permission.",
            _ => $"Couldn't update {item.Name}, even with administrator permission. Reload and try again.",
        };

        // A toggle is all-or-nothing: anything but full success (including "partial") is reported as a failure.
        if (result.Status == ElevationStatus.Succeeded)
        {
            string message = StatusText;
            await LoadAsync();
            StatusText = message;
        }
    }

    private bool CanToggle(StartupItemViewModel? item) => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        LoadCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();
    }
}
