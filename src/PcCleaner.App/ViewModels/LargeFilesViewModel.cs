using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

/// <summary>
/// The large file finder. <see cref="ILargeFileScanner"/> has been implemented and registered in DI since
/// the first release but had no screen, so the feature shipped invisible — the Duplicates tab was even
/// titled "Duplicates &amp; Large Files" while only doing half of that.
/// </summary>
public sealed partial class LargeFilesViewModel : ScanRootsViewModel
{
    private const long DefaultMinSizeBytes = 100L * 1024 * 1024;

    private readonly ILargeFileScanner _scanner;
    private readonly IFileTrasher _trasher;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<FileEntryViewModel> Files { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsDeleting { get; set; }

    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial string SelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nothing selected";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan to find files over 100 MB in the folders below.";

    /// <summary>Read by the dashboard to build the combined reclaimable figure.</summary>
    public long ReclaimableBytesSelected => Files.Where(f => f.IsSelected).Sum(f => f.Model.SizeBytes);

    public long FoundBytes => Files.Sum(f => f.Model.SizeBytes);

    public LargeFilesViewModel(
        ILargeFileScanner scanner,
        IFileTrasher trasher,
        IDialogService dialogs,
        IFolderPickerService folderPicker)
        : base(folderPicker)
    {
        _scanner = scanner;
        _trasher = trasher;
        _dialogs = dialogs;
    }

    protected override void ReportRootPathProblem(string message) => StatusText = message;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (RootPaths.Count == 0)
        {
            StatusText = "Add at least one folder first.";
            return;
        }

        foreach (var file in Files)
        {
            file.PropertyChanged -= OnFilePropertyChanged;
        }

        Files.Clear();
        IsScanning = true;
        StatusText = "Scanning...";
        _scanCts = new CancellationTokenSource();

        // The scanner reports a running file count, never a percentage — so the UI shows the count and an
        // indeterminate bar. Do not turn this into a fake progress fraction.
        var walkProgress = new Progress<int>(count => StatusText = $"Scanning... {count:N0} files read.");

        try
        {
            await foreach (var entry in _scanner.ScanAsync(RootPaths.ToList(), DefaultMinSizeBytes, _scanCts.Token, walkProgress))
            {
                var vm = new FileEntryViewModel(entry);
                vm.PropertyChanged += OnFilePropertyChanged;
                InsertBySizeDescending(vm);
                UpdateShares();
            }

            StatusText = Files.Count == 0
                ? "No files over 100 MB in these folders."
                : $"Found {Files.Count} file(s) over 100 MB, {ByteSizeFormatter.Format(FoundBytes)} in total.";
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
        }
    }

    private bool CanScan() => !IsScanning && !IsDeleting;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var file in Files)
        {
            file.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        long bytes = selected.Sum(f => f.Model.SizeBytes);

        // These are the user's own files, not regenerable junk, so they go to the Recycle Bin — but they
        // still deserve a confirmation that says how much and where it is going.
        bool confirmed = await _dialogs.ConfirmAsync(
            title: $"Move {selected.Count} file(s) to the Recycle Bin?",
            message: $"This frees {ByteSizeFormatter.Format(bytes)}. These are your own files, not junk — you can restore them from the Recycle Bin.",
            acceptText: "Move to Recycle Bin",
            cancelText: "Cancel",
            isDestructive: true,
            details: DialogManifest.Build(selected.Select(f => (f.Model.SizeBytes, System.IO.Path.GetFileName(f.Path)))));

        if (!confirmed)
        {
            return;
        }

        IsDeleting = true;
        StatusText = "Moving to the Recycle Bin...";

        var result = await _trasher.MoveToTrashAsync(selected.Select(f => f.Path).ToList());

        foreach (string path in result.SucceededPaths)
        {
            var vm = Files.FirstOrDefault(f => f.Path == path);
            if (vm is not null)
            {
                vm.PropertyChanged -= OnFilePropertyChanged;
                Files.Remove(vm);
            }
        }

        StatusText = result.AllSucceeded
            ? $"Moved {result.SucceededPaths.Count} file(s) to the Recycle Bin — freed {ByteSizeFormatter.Format(result.BytesFreed)}."
            : $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}; {result.FailedPaths.Count} file(s) could not be moved.";

        IsDeleting = false;
        UpdateSelection();
        UpdateShares();
    }

    private bool CanDeleteSelected() => !IsDeleting && !IsScanning && Files.Any(f => f.IsSelected);

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDeletingChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnFilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileEntryViewModel.IsSelected))
        {
            UpdateSelection();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Keeps the list biggest-first as results stream in. The scanner yields in directory-walk order, which
    /// is meaningless to a user hunting for space — and the whole point of this screen is "what is taking up
    /// the most room". Inserting in place beats re-sorting the whole collection on every arrival.
    /// </summary>
    private void InsertBySizeDescending(FileEntryViewModel file)
    {
        int index = 0;
        while (index < Files.Count && Files[index].Model.SizeBytes >= file.Model.SizeBytes)
        {
            index++;
        }

        Files.Insert(index, file);
    }

    private void UpdateSelection()
    {
        int count = Files.Count(f => f.IsSelected);
        long bytes = ReclaimableBytesSelected;

        SelectedSizeText = ByteSizeFormatter.Format(bytes);
        SelectionSummary = count == 0
            ? $"0 of {Files.Count} selected"
            : $"{count} of {Files.Count} selected · {ByteSizeFormatter.Format(bytes)}";

        OnPropertyChanged(nameof(ReclaimableBytesSelected));
        OnPropertyChanged(nameof(FoundBytes));
    }

    private void UpdateShares()
    {
        long max = Files.Count == 0 ? 0 : Files.Max(f => f.Model.SizeBytes);
        if (max <= 0)
        {
            return;
        }

        foreach (var file in Files)
        {
            file.ShareOfMax = (double)file.Model.SizeBytes / max;
        }
    }
}
