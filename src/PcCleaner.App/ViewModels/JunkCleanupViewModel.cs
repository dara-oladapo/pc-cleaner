using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class JunkCleanupViewModel : ObservableObject
{
    private readonly IJunkScanner _scanner;
    private readonly IJunkCleaner _cleaner;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _scanCts;

    /// <summary>Flat list of every result — the source of truth for totals and for cleaning.</summary>
    public ObservableCollection<JunkItemViewModel> Items { get; } = [];

    /// <summary>The same items arranged by category, which is what the list on screen binds to.</summary>
    public ObservableCollection<JunkCategoryGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    /// <summary>Drives the swap from the "nothing scanned yet" invitation to the results list.</summary>
    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial string SelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nothing selected";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan to find temp files, caches, and logs that are safe to remove.";

    /// <summary>Read by the dashboard for the combined reclaimable figure and the capacity bar.</summary>
    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Model.SizeBytes);

    public JunkCleanupViewModel(IJunkScanner scanner, IJunkCleaner cleaner, IDialogService dialogs)
    {
        _scanner = scanner;
        _cleaner = cleaner;
        _dialogs = dialogs;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        ClearResults();
        IsScanning = true;
        StatusText = "Scanning...";
        _scanCts = new CancellationTokenSource();

        // Reports the running file count within whatever source is currently being sized — the only way to show
        // live movement while a single large folder (e.g. a 28,000-item temp dir) is still being walked. It is a
        // count, not a fraction, so the UI pairs it with an indeterminate bar rather than inventing a percentage.
        var walkProgress = new Progress<int>(count => StatusText = $"Scanning... {count:N0} files in current location.");

        try
        {
            await foreach (var item in _scanner.ScanAsync(_scanCts.Token, walkProgress))
            {
                var vm = new JunkItemViewModel(item);
                vm.PropertyChanged += OnItemPropertyChanged;
                Items.Add(vm);
                AddToGroup(vm);
                UpdateSelection();
                UpdateShares();
                StatusText = $"Scanning... {Items.Count} location(s) found so far.";
            }

            StatusText = Items.Count == 0
                ? "No junk found — you're clean."
                : $"Found {Items.Count} location(s) using {ByteSizeFormatter.Format(Items.Sum(i => i.Model.SizeBytes))}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan stopped. Partial results are shown.";
        }
        finally
        {
            IsScanning = false;
            HasScanned = true;
            UpdateSelection();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void SelectNone() => SetAllSelected(false);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        long bytes = selected.Sum(i => i.Model.SizeBytes);

        // This is a permanent delete — IJunkCleaner does not use the Recycle Bin, because caches are
        // regenerable and filling the bin with them would defeat the point. That makes the confirmation
        // mandatory rather than polite, and it has to say plainly that there is no undo.
        bool confirmed = await _dialogs.ConfirmAsync(
            title: $"Delete {selected.Count} location(s) permanently?",
            message: $"This frees {ByteSizeFormatter.Format(bytes)}. Junk files are rebuilt by the apps that made them, but they do not go to the Recycle Bin, so this cannot be undone.",
            acceptText: "Delete permanently");

        if (!confirmed)
        {
            return;
        }

        IsCleaning = true;
        StatusText = "Cleaning...";

        var result = await _cleaner.CleanAsync(selected.Select(s => s.Model).ToList());

        foreach (string succeededPath in result.SucceededPaths)
        {
            var vm = Items.FirstOrDefault(i => i.Path == succeededPath);
            if (vm is not null)
            {
                vm.PropertyChanged -= OnItemPropertyChanged;
                Items.Remove(vm);
                RemoveFromGroup(vm);
            }
        }

        StatusText = result.AllSucceeded
            ? $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}."
            : $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}; {result.FailedPaths.Count} item(s) could not be removed (may need admin rights).";

        IsCleaning = false;
        UpdateSelection();
        UpdateShares();
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
        if (e.PropertyName == nameof(JunkItemViewModel.IsSelected))
        {
            UpdateSelection();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var item in Items)
        {
            item.IsSelected = selected;
        }
    }

    private void ClearResults()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();
        Groups.Clear();
    }

    private void AddToGroup(JunkItemViewModel item)
    {
        var group = Groups.FirstOrDefault(g => g.Name == item.CategoryText);
        if (group is null)
        {
            group = new JunkCategoryGroup(item.CategoryText);
            Groups.Add(group);
        }

        group.Add(item);
        group.NotifyTotalChanged();
    }

    private void RemoveFromGroup(JunkItemViewModel item)
    {
        var group = Groups.FirstOrDefault(g => g.Contains(item));
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
    }

    private void UpdateShares()
    {
        long max = Items.Count == 0 ? 0 : Items.Max(i => i.Model.SizeBytes);
        if (max <= 0)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.ShareOfMax = (double)item.Model.SizeBytes / max;
        }
    }
}
