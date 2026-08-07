using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;

namespace PcCleaner.App.ViewModels;

public sealed partial class StartupManagerViewModel : ObservableObject
{
    private readonly IStartupItemManager _manager;

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Load to see apps and services that launch automatically.";

    [ObservableProperty]
    public partial string ItemCountText { get; set; } = "0";

    public StartupManagerViewModel(IStartupItemManager manager)
    {
        _manager = manager;
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
        catch (Exception ex)
        {
            StatusText = $"Couldn't update {item.Name}: {ex.Message} (this may need administrator/root privileges).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggle(StartupItemViewModel? item) => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        LoadCommand.NotifyCanExecuteChanged();
        ToggleCommand.NotifyCanExecuteChanged();
    }
}
