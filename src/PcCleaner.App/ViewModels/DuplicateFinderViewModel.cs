using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class DuplicateFinderViewModel : ScanRootsViewModel
{
    private const long MinFileSizeBytes = 4 * 1024;

    private readonly IDuplicateFileScanner _scanner;
    private readonly IFileTrasher _trasher;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsDeleting { get; set; }

    /// <summary>Drives the swap from the "nothing scanned yet" invitation to the results list.</summary>
    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial string ReclaimableText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nothing selected";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan the folders below for files with identical contents.";

    /// <summary>Read by the dashboard for the combined reclaimable figure and the capacity bar.</summary>
    public long ReclaimableBytesSelected => Groups.Sum(g => g.ReclaimableBytesSelected);

    public DuplicateFinderViewModel(
        IDuplicateFileScanner scanner,
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

        foreach (var group in Groups)
        {
            group.PropertyChanged -= OnGroupPropertyChanged;
        }

        Groups.Clear();
        IsScanning = true;
        StatusText = "Scanning for duplicates...";
        _scanCts = new CancellationTokenSource();

        // Duplicate detection has to finish walking every folder before it can start grouping/hashing, so
        // without this the UI would sit frozen on "Scanning..." for the whole walk with no sign of life.
        var walkProgress = new Progress<int>(count => StatusText = $"Scanning... {count:N0} files scanned so far.");

        try
        {
            await foreach (var group in _scanner.ScanAsync(RootPaths.ToList(), MinFileSizeBytes, _scanCts.Token, walkProgress))
            {
                var vm = new DuplicateGroupViewModel(group);
                vm.PropertyChanged += OnGroupPropertyChanged;
                Groups.Add(vm);
                UpdateReclaimable();
                UpdateShares();
            }

            StatusText = Groups.Count == 0
                ? "No duplicates found in these folders."
                : $"Found {Groups.Count} group(s) of identical files.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan stopped. Partial results are shown.";
        }
        finally
        {
            IsScanning = false;
            HasScanned = true;
            UpdateReclaimable();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsScanning && !IsDeleting;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    /// <summary>Deselects every copy, which leaves all of them on disk — the "I'll pick these myself" escape hatch.</summary>
    [RelayCommand]
    private void SelectNone()
    {
        foreach (var file in Groups.SelectMany(g => g.Files))
        {
            file.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var pathsToDelete = Groups
            .SelectMany(g => g.Files.Where(f => f.IsSelected).Select(f => f.Path))
            .ToList();

        if (pathsToDelete.Count == 0)
        {
            return;
        }

        long bytes = ReclaimableBytesSelected;

        var selectedFiles = Groups.SelectMany(g => g.Files.Where(f => f.IsSelected)).ToList();

        bool confirmed = await _dialogs.ConfirmAsync(
            title: $"Move {pathsToDelete.Count} copy/copies to the Recycle Bin?",
            message: $"This frees {ByteSizeFormatter.Format(bytes)}. At least one copy of every file stays where it is, and anything moved can be restored from the Recycle Bin.",
            acceptText: "Move to Recycle Bin",
            cancelText: "Cancel",
            isDestructive: true,
            details: DialogManifest.Build(selectedFiles.Select(f => (f.Model.SizeBytes, System.IO.Path.GetFileName(f.Path)))));

        if (!confirmed)
        {
            return;
        }

        IsDeleting = true;
        StatusText = "Moving to Trash/Recycle Bin...";

        var result = await _trasher.MoveToTrashAsync(pathsToDelete);

        foreach (var group in Groups.ToList())
        {
            group.RemoveFiles(result.SucceededPaths);
            if (group.Files.Count <= 1)
            {
                group.PropertyChanged -= OnGroupPropertyChanged;
                Groups.Remove(group);
            }
        }

        StatusText = result.AllSucceeded
            ? $"Moved {result.SucceededPaths.Count} file(s) to the Recycle Bin — freed {ByteSizeFormatter.Format(result.BytesFreed)}."
            : $"Freed {ByteSizeFormatter.Format(result.BytesFreed)}; {result.FailedPaths.Count} file(s) could not be removed.";

        IsDeleting = false;
        UpdateReclaimable();
        UpdateShares();
    }

    private bool CanDeleteSelected() => !IsDeleting && !IsScanning && Groups.Any(g => g.ReclaimableBytesSelected > 0);

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

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateGroupViewModel.ReclaimableBytesSelected))
        {
            UpdateReclaimable();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateReclaimable()
    {
        long total = ReclaimableBytesSelected;
        int selectedCopies = Groups.Sum(g => g.Files.Count(f => f.IsSelected));
        int totalCopies = Groups.Sum(g => g.Files.Count);

        ReclaimableText = ByteSizeFormatter.Format(total);
        SelectionSummary = $"{selectedCopies} of {totalCopies} copies selected · {ByteSizeFormatter.Format(total)}";

        OnPropertyChanged(nameof(ReclaimableBytesSelected));
    }

    private void UpdateShares()
    {
        long max = Groups.Count == 0 ? 0 : Groups.Max(g => g.Model.ReclaimableBytes);
        if (max <= 0)
        {
            return;
        }

        foreach (var group in Groups)
        {
            group.ShareOfMax = (double)group.Model.ReclaimableBytes / max;
        }
    }
}
