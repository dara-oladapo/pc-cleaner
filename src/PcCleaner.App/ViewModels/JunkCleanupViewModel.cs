using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class JunkCleanupViewModel : ObservableObject
{
    private readonly IJunkScanner _scanner;
    private readonly IJunkCleaner _cleaner;
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<JunkItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial string SelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan to find temp files, caches, and logs safe to remove.";

    public JunkCleanupViewModel(IJunkScanner scanner, IJunkCleaner cleaner)
    {
        _scanner = scanner;
        _cleaner = cleaner;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Items.Clear();
        IsScanning = true;
        StatusText = "Scanning...";
        _scanCts = new CancellationTokenSource();

        // Reports the running file count within whatever source is currently being sized — the only way to show
        // live movement while a single large folder (e.g. a 28,000-item temp dir) is still being walked.
        var walkProgress = new Progress<int>(count => StatusText = $"Scanning... {count:N0} files in current location.");

        try
        {
            await foreach (var item in _scanner.ScanAsync(_scanCts.Token, walkProgress))
            {
                var vm = new JunkItemViewModel(item);
                vm.PropertyChanged += OnItemPropertyChanged;
                Items.Add(vm);
                UpdateSelectedSize();
                UpdateShares();
                StatusText = $"Scanning... {Items.Count} location(s) found so far.";
            }

            StatusText = Items.Count == 0
                ? "No junk found — you're clean."
                : $"Found {Items.Count} location(s) using {ByteSizeFormatter.Format(Items.Sum(i => i.Model.SizeBytes))}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        finally
        {
            IsScanning = false;
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
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
            }
        }

        StatusText = result.AllSucceeded
            ? $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}."
            : $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}; {result.FailedPaths.Count} item(s) could not be removed (may need admin rights).";

        IsCleaning = false;
        UpdateSelectedSize();
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
            UpdateSelectedSize();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateSelectedSize()
    {
        long total = Items.Where(i => i.IsSelected).Sum(i => i.Model.SizeBytes);
        SelectedSizeText = ByteSizeFormatter.Format(total);
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
