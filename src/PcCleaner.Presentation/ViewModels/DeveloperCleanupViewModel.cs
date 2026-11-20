using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Core.Utilities;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// The Developer page: Docker, stale git worktrees and build output. Follows the junk page's pattern (scan,
/// grouped rows with a size meter, bulk selection, confirm, clean), plus a risk and a reason on every row.
/// All work goes through <see cref="DevCleanupService"/>, which refuses to run anything while developer mode is off.
/// </summary>
public sealed partial class DeveloperCleanupViewModel : ObservableObject
{
    private readonly DevCleanupService _service;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ObservableCollection<DevCleanupItemViewModel> Items { get; } = [];

    public ObservableCollection<DevCleanupGroup> Groups { get; } = [];

    /// <summary>Things to know that are not cleanable: the vhdx note, stopped distros, what was kept and why.</summary>
    public ObservableCollection<string> Notes { get; } = [];

    /// <summary>Tool errors, shown verbatim (e.g. a corrupt containerd snapshot).</summary>
    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    [ObservableProperty]
    public partial string SelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nothing selected";

    [ObservableProperty]
    public partial string StatusText { get; set; } =
        "Scan for Docker build cache, unused images, stale git worktrees and old build output.";

    public DeveloperCleanupViewModel(DevCleanupService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
    }

    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Bytes);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        ClearResults();
        if (!_service.IsEnabled)
        {
            StatusText = "Developer mode is off. Turn it on in Settings to use this page.";
            return;
        }

        IsScanning = true;
        StatusText = "Scanning...";
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(message => StatusText = message);

        try
        {
            IReadOnlyList<DevCleanupReport> reports = await _service.MeasureAsync(progress, _cts.Token);
            foreach (DevCleanupReport report in reports)
            {
                foreach (DevCleanupItem item in report.Items)
                {
                    var vm = new DevCleanupItemViewModel(item);
                    vm.PropertyChanged += OnItemPropertyChanged;
                    Items.Add(vm);
                    AddToGroup(vm);
                }

                foreach (VirtualDiskInfo disk in report.VirtualDisks)
                {
                    Notes.Add($"WSL disk '{disk.Name}': {ByteSizeFormatter.Format(disk.SizeBytes)} on disk at {disk.Path}.");
                }

                foreach (string note in report.Notes)
                {
                    Notes.Add($"{report.DisplayName}: {note}");
                }

                foreach (string error in report.Errors)
                {
                    Errors.Add(error);
                }
            }

            StatusText = Items.Count == 0
                ? "Nothing to clean up."
                : $"Found {Items.Count} item(s) using {ByteSizeFormatter.Format(Items.Sum(i => i.Bytes))}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan stopped.";
        }
        finally
        {
            IsScanning = false;
            HasScanned = true;
            HasErrors = Errors.Count > 0;
            HasNotes = Notes.Count > 0;
            UpdateShares();
            UpdateSelection();
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    [RelayCommand]
    private void SelectNone()
    {
        foreach (DevCleanupItemViewModel item in Items)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectDefaults()
    {
        foreach (DevCleanupItemViewModel item in Items)
        {
            item.IsSelected = item.Model.SelectedByDefault;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var warnings = selected.Where(s => s.HasWarning).Select(s => $"{s.Title}: {s.Warning}").ToList();
        string message =
            $"About {ByteSizeFormatter.Format(SelectedBytes)}. Docker prunes and worktree removals cannot be undone, and build "
            + "output is deleted permanently (the next build or install recreates it). Branches are never deleted.";
        if (warnings.Count > 0)
        {
            message += " " + string.Join(" ", warnings);
        }

        bool confirmed = await _dialogs.ConfirmAsync(
            title: $"Run {selected.Count} developer cleanup action(s)?",
            message: message,
            acceptText: "Clean up",
            cancelText: "Cancel",
            isDestructive: true,
            details: DialogManifest.Build(selected.Select(s => (s.Bytes, $"{s.Model.Group}: {s.Title}"))));

        if (!confirmed)
        {
            return;
        }

        IsCleaning = true;
        StatusText = "Cleaning...";
        try
        {
            DevCleanupResult result = await _service.CleanAsync(selected.Select(s => s.Model).ToList());
            var done = result.Succeeded.Select(i => i.Key).ToHashSet();
            foreach (DevCleanupItemViewModel vm in selected.Where(s => done.Contains(s.Model.Key)))
            {
                vm.PropertyChanged -= OnItemPropertyChanged;
                Items.Remove(vm);
                RemoveFromGroup(vm);
            }

            Errors.Clear();
            foreach ((DevCleanupItem _, string error) in result.Failed)
            {
                Errors.Add(error);
            }

            foreach (string line in result.Messages)
            {
                Notes.Add(line);
            }

            HasErrors = Errors.Count > 0;
            HasNotes = Notes.Count > 0;
            StatusText = result.Failed.Count == 0
                ? $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}."
                : $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}; {result.Failed.Count} action(s) did not complete. See the messages below.";
        }
        finally
        {
            IsCleaning = false;
            UpdateShares();
            UpdateSelection();
        }
    }

    private bool CanClean() => !IsCleaning && !IsScanning && Items.Any(i => i.IsSelected);

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCleaningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevCleanupItemViewModel.IsSelected))
        {
            UpdateSelection();
        }
    }

    private void ClearResults()
    {
        foreach (DevCleanupItemViewModel item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();
        Groups.Clear();
        Notes.Clear();
        Errors.Clear();
        HasErrors = false;
        HasNotes = false;
        UpdateSelection();
    }

    private void AddToGroup(DevCleanupItemViewModel item)
    {
        DevCleanupGroup? group = Groups.FirstOrDefault(g => g.Name == item.Model.Group);
        if (group is null)
        {
            group = new DevCleanupGroup(item.Model.Group);
            Groups.Add(group);
        }

        group.Add(item);
        group.NotifyTotalChanged();
    }

    private void RemoveFromGroup(DevCleanupItemViewModel item)
    {
        DevCleanupGroup? group = Groups.FirstOrDefault(g => g.Contains(item));
        if (group is null)
        {
            return;
        }

        group.Remove(item);
        if (group.Count == 0)
        {
            Groups.Remove(group);
        }
        else
        {
            group.NotifyTotalChanged();
        }
    }

    private void UpdateSelection()
    {
        int count = Items.Count(i => i.IsSelected);
        long total = SelectedBytes;
        SelectedSizeText = ByteSizeFormatter.Format(total);
        SelectionSummary = $"{count} of {Items.Count} selected · {ByteSizeFormatter.Format(total)}";
        OnPropertyChanged(nameof(SelectedBytes));
        CleanCommand.NotifyCanExecuteChanged();
    }

    private void UpdateShares()
    {
        long max = Items.Count == 0 ? 0 : Items.Max(i => i.Bytes);
        foreach (DevCleanupItemViewModel item in Items)
        {
            item.ShareOfMax = max <= 0 ? 0 : (double)item.Bytes / max;
        }
    }
}
