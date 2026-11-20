using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// The landing screen: what this computer looks like right now, which volumes and tools a run covers, and
/// the three actions that run them: scan, scan and clean, scan and auto clean.
/// </summary>
/// <remarks>
/// This composes the tool view models rather than re-implementing their scans, which is why they are
/// registered as singletons in <c>MauiProgram</c>. A scan started here fills in the detail pages, and a
/// selection changed on a detail page moves the figures here: one shared state, two views onto it.
/// The run itself, and the rules that keep it safe, live in <see cref="DashboardRunner"/>.
/// </remarks>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDriveSpaceReader _driveSpaceReader;
    private readonly INavigationService _navigation;
    private readonly ScanVolumeSettings _volumeSettings;
    private readonly DeveloperModeSettings _developerMode;
    private readonly DashboardRunner _runner;
    private readonly DashboardToolViewModel _developerTool;
    private CancellationTokenSource? _runCts;
    private bool _writingVolume;

    /// <summary>
    /// Every volume the large-file and duplicate scans can walk, ticked or not, including a ticked one that
    /// isn't connected. The same list and the same setting as the Settings page.
    /// </summary>
    public ObservableCollection<VolumeChoiceViewModel> Volumes { get; } = [];

    /// <summary>The tools a run can cover, each with its include tick and its parts.</summary>
    public ObservableCollection<DashboardToolViewModel> Tools { get; } = [];

    /// <summary>The tools the current run covers, one progress marker each.</summary>
    public ObservableCollection<DashboardToolViewModel> RunSteps { get; } = [];

    /// <summary>What the last clean did, one line per tool, plus anything that went wrong.</summary>
    public ObservableCollection<string> RunReport { get; } = [];

    [ObservableProperty]
    public partial bool HasVolumes { get; set; }

    [ObservableProperty]
    public partial string VolumesStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRunReport { get; set; }

    public JunkCleanupViewModel Junk { get; }

    public DuplicateFinderViewModel Duplicates { get; }

    public LargeFilesViewModel LargeFiles { get; }

    public CloudDrivesViewModel Cloud { get; }

    public DeveloperCleanupViewModel Developer { get; }

    public StartupManagerViewModel Startup { get; }

    /// <summary>True while tools are being read. The run can be stopped during this phase.</summary>
    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    /// <summary>True while tools are being cleaned. A clean is not stoppable part-way.</summary>
    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    public bool IsBusy => IsScanning || IsCleaning;

    public bool IsIdle => !IsBusy;

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
    public partial string StatusText { get; set; } = "Scan to see what can be freed.";

    /// <summary>"Scanning Junk Files · 1 of 5 finished". A count of tools, which is a real fraction.</summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    public string StartupSummaryText =>
        Startup.Items.Count == 0
            ? "Not read yet"
            : $"{Startup.Items.Count(i => i.IsEnabled)} of {Startup.Items.Count} launch at login";

    private DriveSpace? _drive;

    public DashboardViewModel(
        IDriveSpaceReader driveSpaceReader,
        INavigationService navigation,
        ScanVolumeSettings volumeSettings,
        DeveloperModeSettings developerMode,
        IDialogService dialogs,
        JunkCleanupViewModel junk,
        DuplicateFinderViewModel duplicates,
        LargeFilesViewModel largeFiles,
        CloudDrivesViewModel cloud,
        DeveloperCleanupViewModel developer,
        StartupManagerViewModel startup)
    {
        _driveSpaceReader = driveSpaceReader;
        _navigation = navigation;
        _volumeSettings = volumeSettings;
        _developerMode = developerMode;
        _runner = new DashboardRunner(dialogs);
        Junk = junk;
        Duplicates = duplicates;
        LargeFiles = largeFiles;
        Cloud = cloud;
        Developer = developer;
        Startup = startup;

        Tools.Add(Track(BuildJunkTool()));
        Tools.Add(Track(BuildDuplicatesTool()));
        Tools.Add(Track(BuildLargeFilesTool()));
        Tools.Add(Track(BuildCloudTool()));
        _developerTool = Track(BuildDeveloperTool());
        Tools.Add(Track(BuildStartupTool()));
        SyncDeveloperTool();

        // The figures have to move when a checkbox is ticked on any detail page, not just when a run
        // finishes here.
        Junk.PropertyChanged += OnToolChanged;
        Duplicates.PropertyChanged += OnToolChanged;
        LargeFiles.PropertyChanged += OnToolChanged;
        Cloud.PropertyChanged += OnToolChanged;
        Developer.PropertyChanged += OnToolChanged;
        Startup.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StartupSummaryText));
        _volumeSettings.Changed += (_, _) => RefreshVolumes();
        _developerMode.Changed += (_, _) => SyncDeveloperTool();

        RefreshDrive();
    }

    /// <summary>
    /// What a clean started now would free: everything ticked in the tools that are included. Startup frees
    /// nothing, so it is never counted.
    /// </summary>
    public long TotalReclaimableBytes =>
        Tools.Where(t => t.IsIncluded && t.CanClean).Sum(t => t.SelectedBytes());

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ScanEverythingAsync() => RunAsync(DashboardRunMode.ScanOnly);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ScanAndCleanAsync() => RunAsync(DashboardRunMode.ScanAndClean);

    /// <summary>
    /// The one action in the app that removes things without a confirmation. It removes exactly what is
    /// ticked after the scan, by each tool's own default rules, and nothing else.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ScanAndAutoCleanAsync() => RunAsync(DashboardRunMode.ScanAndAutoClean);

    private async Task RunAsync(DashboardRunMode mode)
    {
        IsScanning = true;
        RunReport.Clear();
        HasRunReport = false;
        _runCts = new CancellationTokenSource();
        StatusText = "Reading the ticked tools...";

        RunSteps.Clear();
        foreach (DashboardToolViewModel tool in Tools.Where(t => t.IsIncluded))
        {
            tool.IsStepDone = false;
            RunSteps.Add(tool);
        }

        try
        {
            DashboardRunResult result = await _runner.RunAsync(Tools.ToList(), mode, _runCts.Token, OnRunProgress);

            foreach (DashboardToolViewModel tool in result.Cleaned)
            {
                RunReport.Add($"{tool.Title}: {tool.Outcome()}");
            }

            foreach (string problem in result.Problems)
            {
                RunReport.Add(problem);
            }

            HasRunReport = RunReport.Count > 0;
            StatusText = Describe(result, mode);
        }
        finally
        {
            IsScanning = false;
            IsCleaning = false;
            ProgressText = string.Empty;
            RefreshDrive();
            RefreshTools();
        }
    }

    private void OnRunProgress(DashboardRunProgress progress)
    {
        if (progress.IsCleaning && !IsCleaning)
        {
            // The scan is over, so there is nothing left to stop.
            IsCleaning = true;
            IsScanning = false;
            StatusText = "Cleaning what is ticked...";
        }

        string verb = progress.IsCleaning ? "Cleaning" : "Scanning";
        ProgressText = $"{verb} {progress.Label} · {progress.Finished} of {progress.Total} finished";
        RecomputeTotals();
    }

    private string Describe(DashboardRunResult result, DashboardRunMode mode)
    {
        string problems = result.Problems.Count > 0 ? " Some steps had problems, listed below." : string.Empty;

        return result.Outcome switch
        {
            DashboardRunOutcome.NothingIncluded => "Tick at least one tool to scan.",
            DashboardRunOutcome.Stopped => mode == DashboardRunMode.ScanOnly
                ? "Scan stopped. Partial results are shown."
                : "Scan stopped. Partial results are shown and nothing was removed.",
            DashboardRunOutcome.Declined => "Nothing was removed.",
            DashboardRunOutcome.NothingToClean => "Nothing is ticked, so nothing was removed." + problems,
            DashboardRunOutcome.Cleaned => $"Clean finished across {result.Cleaned.Count} tool(s)." + problems,
            _ => (TotalReclaimableBytes > 0
                ? $"{ByteSizeFormatter.Format(TotalReclaimableBytes)} ticked. Review the tools below, then clean."
                : "Nothing to reclaim. This computer is already clean.") + problems,
        };
    }

    /// <summary>Opens a tool's page from its row.</summary>
    [RelayCommand]
    private Task OpenToolAsync(string route) => _navigation.GoToToolAsync(route);

    [RelayCommand]
    private void CancelScan()
    {
        _runCts?.Cancel();

        // Each tool owns its own cancellation token, so stopping the run means telling whichever one is
        // currently walking the disk to stop too.
        foreach (DashboardToolViewModel tool in Tools)
        {
            tool.Cancel();
        }
    }

    partial void OnIsScanningChanged(bool value) => NotifyBusyChanged();

    partial void OnIsCleaningChanged(bool value) => NotifyBusyChanged();

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        ScanEverythingCommand.NotifyCanExecuteChanged();
        ScanAndCleanCommand.NotifyCanExecuteChanged();
        ScanAndAutoCleanCommand.NotifyCanExecuteChanged();
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JunkCleanupViewModel.SelectedBytes)
            or nameof(DuplicateFinderViewModel.ReclaimableBytesSelected)
            or nameof(JunkCleanupViewModel.HasScanned))
        {
            RefreshTools();
        }
    }

    /// <summary>Re-reads every tool's figure and ticks. Public so the page can refresh on appearing.</summary>
    public void RefreshTools()
    {
        foreach (DashboardToolViewModel tool in Tools)
        {
            tool.Refresh();
        }

        RecomputeTotals();
    }

    /// <summary>
    /// Rebuilds every tool's parts from its lists. Run when the page appears, because a scan or a clean
    /// done on a tool's own page changes what there is to list.
    /// </summary>
    public void RebuildTools()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (DashboardToolViewModel tool in Tools)
        {
            tool.Rebuild();
        }

        RecomputeTotals();
    }

    /// <summary>Re-reads the volumes. Public so the page can refresh it on appearing.</summary>
    public void RefreshVolumes()
    {
        // A tick made here writes the setting, which raises Changed. Rebuilding the list from inside that
        // would replace the very row whose checkbox is still being handled.
        if (_writingVolume)
        {
            return;
        }

        var snapshot = _volumeSettings.ReadVolumes();

        Volumes.Clear();
        foreach (var choice in _volumeSettings.GetChoices(snapshot))
        {
            Volumes.Add(new VolumeChoiceViewModel(choice, OnVolumeToggled));
        }

        HasVolumes = Volumes.Count > 0;
        UpdateVolumesStatus();
    }

    private void OnVolumeToggled(VolumeChoiceViewModel volume, bool selected)
    {
        _writingVolume = true;
        try
        {
            _volumeSettings.SetSelected(volume.RootPath, selected);
        }
        finally
        {
            _writingVolume = false;
        }

        UpdateVolumesStatus();
    }

    private void UpdateVolumesStatus()
    {
        int selected = Volumes.Count(v => v.IsSelected);
        int missing = Volumes.Count(v => v.IsSelected && !v.IsAvailable);

        VolumesStatusText = selected == 0
            ? "No volume ticked. Large Files and Duplicates will have nothing to look at."
            : missing > 0
                ? $"{selected} of {Volumes.Count} ticked, {missing} not connected right now."
                : $"{selected} of {Volumes.Count} ticked. Large Files and Duplicates look at these.";
    }

    private void RefreshDrive()
    {
        RefreshVolumes();

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

    private DashboardToolViewModel Track(DashboardToolViewModel tool)
    {
        tool.IncludedChanged += (_, _) => RecomputeTotals();
        return tool;
    }

    /// <summary>The Developer tool is listed only while developer mode is on, between Cloud Drives and Startup.</summary>
    private void SyncDeveloperTool()
    {
        bool listed = Tools.Contains(_developerTool);
        if (_developerMode.IsEnabled && !listed)
        {
            Tools.Insert(Tools.Count - 1, _developerTool);
        }
        else if (!_developerMode.IsEnabled && listed)
        {
            Tools.Remove(_developerTool);
        }

        RecomputeTotals();
    }

    private static string Ticked(bool hasScanned, string sizeText) =>
        hasScanned ? $"{sizeText} ticked" : "Not scanned yet";

    private DashboardToolViewModel BuildJunkTool() => new()
    {
        Route = "junk",
        Title = "Junk Files",
        Caption = "Deleted permanently",
        Scan = () => Junk.ScanCommand.ExecuteAsync(null),
        Clean = Junk.CleanSelectedAsync,
        Cancel = () => Junk.CancelScanCommand.Execute(null),
        SelectedBytes = () => Junk.SelectedBytes,
        SelectedCount = () => Junk.Items.Count(i => i.IsSelected),
        Summary = () => Ticked(Junk.HasScanned, Junk.SelectedSizeText),
        Outcome = () => Junk.StatusText,
        BuildParts = () => (
            Junk.Groups.Take(DashboardToolViewModel.MaxParts).Select(group => new DashboardPartViewModel(
                title: group.Name,
                detail: $"{group.Count} location(s)",
                isSelected: () => group.Any(i => i.IsSelected),
                setSelected: value => SetJunkCategory(group, value),
                sizeText: () => $"{ByteSizeFormatter.Format(group.Where(i => i.IsSelected).Sum(i => i.Model.SizeBytes))} of {group.TotalText}")).ToList(),
            Junk.Groups.Count),
    };

    /// <summary>
    /// Ticking a category restores the scanner's own picks, so the large, slow-to-rebuild stores it leaves
    /// unticked stay unticked. A category made only of those is ticked whole, because that is what was asked.
    /// </summary>
    private static void SetJunkCategory(JunkCategoryGroup group, bool selected)
    {
        bool anyDefault = group.Any(i => i.Model.SelectedByDefault);
        foreach (JunkItemViewModel item in group)
        {
            item.IsSelected = selected && (!anyDefault || item.Model.SelectedByDefault);
        }
    }

    private DashboardToolViewModel BuildDuplicatesTool() => new()
    {
        Route = "duplicates",
        Title = "Duplicates",
        Caption = "Moved to the Recycle Bin",
        Scan = () => Duplicates.ScanCommand.ExecuteAsync(null),
        Clean = Duplicates.CleanSelectedAsync,
        Cancel = () => Duplicates.CancelScanCommand.Execute(null),
        SelectedBytes = () => Duplicates.ReclaimableBytesSelected,
        SelectedCount = () => Duplicates.Groups.Sum(g => g.Files.Count(f => f.IsSelected)),
        Summary = () => Ticked(Duplicates.HasScanned, Duplicates.ReclaimableText),
        Outcome = () => Duplicates.StatusText,
        BuildParts = () => (
            Duplicates.Groups
                .OrderByDescending(g => g.Model.ReclaimableBytes)
                .Take(DashboardToolViewModel.MaxParts)
                .Select(group => new DashboardPartViewModel(
                    title: group.Files.Count > 0 ? Path.GetFileName(group.Files[0].Path) : "Identical files",
                    detail: $"{group.Files.Count} copies · {group.SizeEachText} each",
                    isSelected: () => group.Files.Any(f => f.IsSelected),
                    setSelected: value => SetDuplicateGroup(group, value),
                    sizeText: () => ByteSizeFormatter.Format(group.ReclaimableBytesSelected))).ToList(),
            Duplicates.Groups.Count),
    };

    /// <summary>Ticking a group selects every copy but the first, so one copy of the file always stays.</summary>
    private static void SetDuplicateGroup(DuplicateGroupViewModel group, bool selected)
    {
        for (int i = 0; i < group.Files.Count; i++)
        {
            group.Files[i].IsSelected = selected && i > 0;
        }
    }

    private DashboardToolViewModel BuildLargeFilesTool() => new()
    {
        Route = "large",
        Title = "Large Files",
        Caption = "Moved to the Recycle Bin",
        Scan = () => LargeFiles.ScanCommand.ExecuteAsync(null),
        Clean = LargeFiles.CleanSelectedAsync,
        Cancel = () => LargeFiles.CancelScanCommand.Execute(null),
        SelectedBytes = () => LargeFiles.ReclaimableBytesSelected,
        SelectedCount = () => LargeFiles.Files.Count(f => f.IsSelected),
        Summary = () => LargeFiles.HasScanned
            ? $"{LargeFiles.SelectedSizeText} ticked of {ByteSizeFormatter.Format(LargeFiles.FoundBytes)} found"
            : "Not scanned yet",
        Outcome = () => LargeFiles.StatusText,
        BuildParts = () => (
            LargeFiles.Files.Take(DashboardToolViewModel.MaxParts).Select(file => new DashboardPartViewModel(
                title: Path.GetFileName(file.Path),
                detail: file.IsCloudOnly
                    ? $"Cloud only, in {file.CloudRoot!.DisplayName}. Never deleted here."
                    : $"{Path.GetDirectoryName(file.Path)} · last changed {file.LastModifiedText}",
                isSelected: () => file.IsSelected,
                setSelected: value => file.IsSelected = value,
                sizeText: () => file.SizeText,
                canSelect: !file.IsCloudOnly)).ToList(),
            LargeFiles.Files.Count),
    };

    private DashboardToolViewModel BuildCloudTool() => new()
    {
        Route = "cloud",
        Title = "Cloud Drives",
        Caption = "Made online-only, nothing deleted",
        Scan = () => Cloud.ScanCommand.ExecuteAsync(null),
        Clean = Cloud.CleanSelectedAsync,
        Cancel = () => Cloud.CancelScanCommand.Execute(null),
        SelectedBytes = () => Cloud.SelectedBytes,
        SelectedCount = () => Cloud.Roots.Count(r => r.IsSelected && r.SelectedBytes > 0),
        // Folders arrive one at a time and count towards the total straight away, so the row says so too.
        Summary = () => Ticked(Cloud.HasScanned || Cloud.SelectedBytes > 0, Cloud.SelectedSizeText),
        Outcome = () => Cloud.HasLastRun ? Cloud.LastRunText : Cloud.StatusText,
        BuildParts = () => (
            Cloud.Roots.Take(DashboardToolViewModel.MaxParts).Select(root => new DashboardPartViewModel(
                title: root.Title,
                detail: root.IsReadOnly ? root.ReadOnlyText : root.EligibleText,
                isSelected: () => root.IsSelected,
                setSelected: value => root.IsSelected = value,
                sizeText: () => ByteSizeFormatter.Format(root.SelectedBytes),
                canSelect: root.CanSelect)).ToList(),
            Cloud.Roots.Count),
    };

    private DashboardToolViewModel BuildDeveloperTool() => new()
    {
        Route = "developer",
        Title = "Developer",
        Caption = "Pruned or deleted permanently",
        Scan = () => Developer.ScanCommand.ExecuteAsync(null),
        Clean = Developer.CleanSelectedAsync,
        Cancel = () => Developer.CancelScanCommand.Execute(null),
        SelectedBytes = () => Developer.SelectedBytes,
        SelectedCount = () => Developer.Items.Count(i => i.IsSelected),
        Summary = () => Ticked(Developer.HasScanned, Developer.SelectedSizeText),
        Outcome = () => Developer.StatusText,
        BuildParts = () => (
            Developer.Items
                .OrderByDescending(i => i.Bytes)
                .Take(DashboardToolViewModel.MaxParts)
                .Select(item => new DashboardPartViewModel(
                    title: $"{item.Model.Group}: {item.Title}",
                    detail: item.HasWarning ? $"{item.RiskText}. {item.Warning}" : item.RiskText,
                    isSelected: () => item.IsSelected,
                    setSelected: value => item.IsSelected = value,
                    sizeText: () => item.SizeText)).ToList(),
            Developer.Items.Count),
    };

    private DashboardToolViewModel BuildStartupTool() => new()
    {
        Route = "startup",
        Title = "Startup",
        Caption = "Read only, nothing to clean",
        CanClean = false,
        Scan = () => Startup.LoadCommand.ExecuteAsync(null),
        Summary = () => StartupSummaryText,
        BuildParts = () => (
            Startup.Items.Take(DashboardToolViewModel.MaxParts).Select(item => new DashboardPartViewModel(
                title: item.Name,
                detail: string.Empty,
                isSelected: () => false,
                setSelected: _ => { },
                sizeText: () => item.IsEnabled ? "Launches at login" : "Off",
                canSelect: false)).ToList(),
            Startup.Items.Count),
    };
}
