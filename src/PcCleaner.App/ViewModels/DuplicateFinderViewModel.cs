using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class DuplicateFinderViewModel : ObservableObject
{
    private const long MinFileSizeBytes = 4 * 1024;

    private readonly IDuplicateFileScanner _scanner;
    private readonly IFileTrasher _trasher;
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<string> RootPaths { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsDeleting { get; set; }

    [ObservableProperty]
    public partial string ReclaimableText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Add folders, then scan for duplicate files.";

    [ObservableProperty]
    public partial string NewFolderPath { get; set; } = string.Empty;

    public DuplicateFinderViewModel(IDuplicateFileScanner scanner, IFileTrasher trasher)
    {
        _scanner = scanner;
        _trasher = trasher;

        foreach (string folder in DefaultFolders())
        {
            RootPaths.Add(folder);
        }
    }

    private static IEnumerable<string> DefaultFolders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
        ];

        return candidates.Where(Directory.Exists).Distinct();
    }

    [RelayCommand]
    private void AddFolder()
    {
        string path = NewFolderPath.Trim();
        if (path.Length == 0)
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            StatusText = $"'{path}' isn't a folder that exists.";
            return;
        }

        if (!RootPaths.Contains(path))
        {
            RootPaths.Add(path);
        }

        NewFolderPath = string.Empty;
    }

    [RelayCommand]
    private void RemoveFolder(string path) => RootPaths.Remove(path);

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
                ? "No duplicates found."
                : $"Found {Groups.Count} duplicate group(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        finally
        {
            IsScanning = false;
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsScanning && !IsDeleting;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

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
            ? $"Moved {result.SucceededPaths.Count} file(s) to Trash — freed {ByteSizeFormatter.Format(result.BytesFreed)}."
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
        long total = Groups.Sum(g => g.ReclaimableBytesSelected);
        ReclaimableText = ByteSizeFormatter.Format(total);
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
