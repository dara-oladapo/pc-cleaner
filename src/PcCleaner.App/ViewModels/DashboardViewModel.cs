using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

/// <summary>
/// The landing screen: what this computer looks like right now, and one button that reads every tool.
/// </summary>
/// <remarks>
/// This composes the four tool view models rather than re-implementing their scans, which is why they are
/// registered as singletons in <c>MauiProgram</c>. A scan started here fills in the detail pages, and a
/// selection changed on a detail page moves the figure here — one shared state, two views onto it.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDriveSpaceReader _driveSpaceReader;
    private CancellationTokenSource? _runCts;

    public JunkCleanupViewModel Junk { get; }

    public DuplicateFinderViewModel Duplicates { get; }

    public LargeFilesViewModel LargeFiles { get; }

    public StartupManagerViewModel Startup { get; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string DriveNameText { get; set; } = "This computer";

    [ObservableProperty]
    public partial string CapacityText { get; set; } = string.Empty;

    /// <summary>False when the drive can't be read; the page hides the capacity bar rather than drawing a lie.</summary>
    [ObservableProperty]
    public partial bool HasDriveInfo { get; set; }

    [ObservableProperty]
    public partial double OccupiedShare { get; set; }

    [ObservableProperty]
    public partial double ReclaimableShare { get; set; }

    [ObservableProperty]
    public partial double FreeShare { get; set; }

    [ObservableProperty]
    public partial string OccupiedText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string ReclaimableText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string FreeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan everything to see what can be freed.";

    /// <summary>How many of the four tools have finished this run — the one place a determinate figure is honest.</summary>
    [ObservableProperty]
    public partial int CompletedSteps { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    public bool Step1Done => CompletedSteps >= 1;

    public bool Step2Done => CompletedSteps >= 2;

    public bool Step3Done => CompletedSteps >= 3;

    public bool Step4Done => CompletedSteps >= 4;

    public string StartupSummaryText =>
        Startup.Items.Count == 0
            ? "Not read yet"
            : $"{Startup.Items.Count(i => i.IsEnabled)} launch at login";

    private DriveSpace? _drive;

    public DashboardViewModel(
        IDriveSpaceReader driveSpaceReader,
        JunkCleanupViewModel junk,
        DuplicateFinderViewModel duplicates,
        LargeFilesViewModel largeFiles,
        StartupManagerViewModel startup)
    {
        _driveSpaceReader = driveSpaceReader;
        Junk = junk;
        Duplicates = duplicates;
        LargeFiles = largeFiles;
        Startup = startup;

        // The combined figure has to move when a checkbox is ticked on any detail page, not just when a
        // scan finishes here.
        Junk.PropertyChanged += OnToolChanged;
        Duplicates.PropertyChanged += OnToolChanged;
        LargeFiles.PropertyChanged += OnToolChanged;
        Startup.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StartupSummaryText));

        RefreshDrive();
    }

    /// <summary>Total selected across the three tools that actually free space. Startup doesn't, so it isn't counted.</summary>
    public long TotalReclaimableBytes =>
        Junk.SelectedBytes + Duplicates.ReclaimableBytesSelected + LargeFiles.ReclaimableBytesSelected;

    [RelayCommand(CanExecute = nameof(CanScanEverything))]
    private async Task ScanEverythingAsync()
    {
        IsScanning = true;
        CompletedSteps = 0;
        _runCts = new CancellationTokenSource();
        StatusText = "Reading every tool...";

        try
        {
            // Sequential, not parallel. These are all disk-bound walks over the same drive; running them at
            // once makes every one of them slower, and it would make "N of 4 finished" meaningless.
            await RunStepAsync("Junk files", () => Junk.ScanCommand.ExecuteAsync(null));
            await RunStepAsync("Duplicates", () => Duplicates.ScanCommand.ExecuteAsync(null));
            await RunStepAsync("Large files", () => LargeFiles.ScanCommand.ExecuteAsync(null));
            await RunStepAsync("Startup", () => Startup.LoadCommand.ExecuteAsync(null));

            StatusText = TotalReclaimableBytes > 0
                ? $"{ByteSizeFormatter.Format(TotalReclaimableBytes)} selected across three tools. Open a tool to review before removing anything."
                : "Nothing to reclaim — this computer is already clean.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan stopped. Partial results are shown.";
        }
        finally
        {
            IsScanning = false;
            ProgressText = string.Empty;
            RefreshDrive();
            RecomputeTotals();
        }
    }

    // Takes a factory, not a Task: the step must not start until the cancellation check has passed, and an
    // already-started Task would have been running since the argument was evaluated.
    private async Task RunStepAsync(string label, Func<Task> work)
    {
        _runCts?.Token.ThrowIfCancellationRequested();
        ProgressText = $"{label} · {CompletedSteps} of 4 finished";
        await work();
        CompletedSteps++;
        ProgressText = $"{label} · {CompletedSteps} of 4 finished";
        RecomputeTotals();
    }

    private bool CanScanEverything() => !IsScanning;

    /// <summary>Opens a tool from its summary card. The card is the whole target, not a "view details" link.</summary>
    [RelayCommand]
    private Task OpenToolAsync(string route) => Shell.Current.GoToAsync($"//{route}");

    [RelayCommand]
    private void CancelScan()
    {
        _runCts?.Cancel();

        // Each tool owns its own cancellation token, so stopping the run means telling whichever one is
        // currently walking the disk to stop too.
        Junk.CancelScanCommand.Execute(null);
        Duplicates.CancelScanCommand.Execute(null);
        LargeFiles.CancelScanCommand.Execute(null);
    }

    partial void OnIsScanningChanged(bool value) => ScanEverythingCommand.NotifyCanExecuteChanged();

    partial void OnCompletedStepsChanged(int value)
    {
        OnPropertyChanged(nameof(Step1Done));
        OnPropertyChanged(nameof(Step2Done));
        OnPropertyChanged(nameof(Step3Done));
        OnPropertyChanged(nameof(Step4Done));
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JunkCleanupViewModel.SelectedBytes)
            or nameof(DuplicateFinderViewModel.ReclaimableBytesSelected)
            or nameof(LargeFilesViewModel.ReclaimableBytesSelected))
        {
            RecomputeTotals();
        }
    }

    private void RefreshDrive()
    {
        _drive = _driveSpaceReader.Read();
        HasDriveInfo = _drive is not null;

        if (_drive is null)
        {
            DriveNameText = "This computer";
            CapacityText = string.Empty;
            return;
        }

        DriveNameText = $"{_drive.Name} · {ByteSizeFormatter.Format(_drive.TotalBytes)} drive";
        CapacityText = $"{ByteSizeFormatter.Format(_drive.FreeBytes)} free of {ByteSizeFormatter.Format(_drive.TotalBytes)}";
        RecomputeTotals();
    }

    private void RecomputeTotals()
    {
        long reclaimable = TotalReclaimableBytes;
        ReclaimableText = ByteSizeFormatter.Format(reclaimable);
        OnPropertyChanged(nameof(TotalReclaimableBytes));
        OnPropertyChanged(nameof(StartupSummaryText));

        if (_drive is null)
        {
            return;
        }

        OccupiedShare = _drive.OccupiedShare(reclaimable);
        ReclaimableShare = _drive.ReclaimableShare(reclaimable);
        FreeShare = _drive.FreeShare;

        OccupiedText = ByteSizeFormatter.Format(Math.Max(0, _drive.UsedBytes - Math.Clamp(reclaimable, 0, _drive.UsedBytes)));
        FreeText = ByteSizeFormatter.Format(_drive.FreeBytes);
    }
}
