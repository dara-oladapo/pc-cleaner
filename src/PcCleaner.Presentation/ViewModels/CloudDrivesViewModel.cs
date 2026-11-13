using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using PcCleaner.Core.Utilities;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// The Cloud drives page. It frees disk space held by OneDrive, Dropbox, iCloud Drive and similar folders
/// by returning downloaded files that are safely in the cloud to online-only.
/// </summary>
/// <remarks>
/// Nothing on this page deletes anything. A released file stays in the cloud, keeps its place in the folder,
/// and downloads again when it is opened. The decisions about which files qualify live in
/// <see cref="CloudSpaceService"/>; this class only presents them and asks before acting.
/// </remarks>
public sealed partial class CloudDrivesViewModel : ObservableObject
{
    private readonly CloudSpaceService _service;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _scanCts;

    /// <summary>The minimum age the current results were computed with. A release re-checks with the same value.</summary>
    private TimeSpan _scannedMinimumAge = CloudEligibilityRules.DefaultMinimumAge;

    public CloudDrivesViewModel(CloudSpaceService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
    }

    public ObservableCollection<CloudRootViewModel> Roots { get; } = [];

    /// <summary>Choices for "not used for at least N days".</summary>
    public IReadOnlyList<int> AgeOptions { get; } = [7, 30, 90, 180, 365];

    [ObservableProperty]
    public partial int SelectedAgeDays { get; set; } = (int)CloudEligibilityRules.DefaultMinimumAge.TotalDays;

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsFreeing { get; set; }

    [ObservableProperty]
    public partial bool HasScanned { get; set; }

    [ObservableProperty]
    public partial string SelectedSizeText { get; set; } = "0 B";

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Nothing selected";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Scan to see how much of your cloud drives is downloaded to this device.";

    /// <summary>The outcome of the last free-up run. Kept apart from the status line, which the rescan overwrites.</summary>
    [ObservableProperty]
    public partial string LastRunText { get; set; } = string.Empty;

    public bool HasLastRun => LastRunText.Length > 0;

    public long SelectedBytes => Roots.Sum(r => r.SelectedBytes);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        ClearResults();
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        CancellationToken token = _scanCts.Token;
        _scannedMinimumAge = TimeSpan.FromDays(SelectedAgeDays);
        StatusText = "Looking for cloud drives...";

        var problems = new List<string>();

        try
        {
            IReadOnlyList<CloudSyncRoot> roots = await Task.Run(_service.GetSyncRoots, token);
            if (roots.Count == 0)
            {
                StatusText = "No cloud drives found on this device.";
                return;
            }

            foreach (CloudSyncRoot root in roots)
            {
                token.ThrowIfCancellationRequested();
                string name = root.DisplayName;
                StatusText = $"Scanning {name}...";
                var progress = new Progress<int>(count => StatusText = $"Scanning {name}... {count:N0} files looked at.");

                try
                {
                    CloudRootScan scan = await _service.ScanAsync(root, _scannedMinimumAge, token, progress);
                    var vm = new CloudRootViewModel(scan);
                    vm.SelectionChanged += OnRootSelectionChanged;
                    Roots.Add(vm);
                    UpdateShares();
                    UpdateSelection();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{name} could not be read.");
                }
            }

            long downloaded = Roots.Sum(r => r.Scan.HydratedBytes);
            long eligible = Roots.Where(r => r.Root.CanFreeUp).Sum(r => r.Scan.EligibleBytes);
            StatusText = $"{ByteSizeFormatter.Format(downloaded)} downloaded across {Roots.Count} cloud folder(s). {ByteSizeFormatter.Format(eligible)} can be freed.";
            if (problems.Count > 0)
            {
                StatusText += " " + string.Join(" ", problems);
            }
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

    private bool CanScan() => !IsScanning && !IsFreeing;

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanFreeUp))]
    private async Task FreeUpAsync()
    {
        var selected = Roots.Where(r => r.IsSelected && r.Root.CanFreeUp).ToList();
        var requests = selected.SelectMany(r => r.SelectedRequests()).ToList();
        if (requests.Count == 0)
        {
            return;
        }

        long bytes = SelectedBytes;
        string providers = string.Join(", ", selected.Select(r => r.ProviderText).Distinct());

        bool confirmed = await _dialogs.ConfirmAsync(
            title: $"Free up {ByteSizeFormatter.Format(bytes)} on this device?",
            message: $"{requests.Count:N0} file(s) become online-only. They stay in {providers} and download again when you open them. Nothing is deleted. " +
                     "You need an internet connection to open them afterwards. Each file is checked again first; any that changed, were opened, or were set to always stay on this device since the scan are left alone.",
            acceptText: "Free up space",
            cancelText: "Cancel",
            isDestructive: false,
            details: DialogManifest.Build(selected.Select(r => (r.SelectedBytes, r.Title))));

        if (!confirmed)
        {
            return;
        }

        IsFreeing = true;
        StatusText = "Asking the cloud providers to free up space...";
        var progress = new Progress<int>(count => StatusText = $"Asking the cloud providers to free up space... {count:N0} of {requests.Count:N0} files.");

        try
        {
            CloudReleaseResult result = await _service.ReleaseAsync(requests, _scannedMinimumAge, CancellationToken.None, progress);
            LastRunText = Describe(result);
        }
        finally
        {
            IsFreeing = false;
        }

        // The sizes have changed; show what is true now.
        await ScanAsync();
    }

    private bool CanFreeUp() => !IsScanning && !IsFreeing && Roots.Any(r => r.IsSelected && r.SelectedBytes > 0);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (CloudRootViewModel root in Roots.Where(r => r.CanSelect))
        {
            root.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (CloudRootViewModel root in Roots)
        {
            root.IsSelected = false;
        }
    }

    partial void OnSelectedAgeDaysChanged(int value)
    {
        if (HasScanned && TimeSpan.FromDays(value) != _scannedMinimumAge)
        {
            StatusText = "Scan again to apply the new age setting.";
        }
    }

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        FreeUpCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFreeingChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        FreeUpCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastRunTextChanged(string value) => OnPropertyChanged(nameof(HasLastRun));

    internal static string Describe(CloudReleaseResult result)
    {
        var parts = new List<string> { $"Freed {ByteSizeFormatter.Format(result.BytesReclaimed)}." };

        if (result.PendingPaths.Count > 0)
        {
            parts.Add($"{ByteSizeFormatter.Format(result.BytesPending)} in {result.PendingPaths.Count:N0} file(s) is marked online-only and the provider will release it shortly.");
        }

        if (result.Skipped.Count > 0)
        {
            string reasons = string.Join(", ", result.Skipped
                .GroupBy(s => s.SkipReason)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count():N0} {CloudEligibilityRules.Describe(g.Key).ToLowerInvariant()}"));
            parts.Add($"Left alone: {reasons}.");
        }

        if (result.Failed.Count > 0)
        {
            parts.Add($"{result.Failed.Count:N0} file(s) could not be changed.");
        }

        return string.Join(" ", parts);
    }

    private void OnRootSelectionChanged(object? sender, EventArgs e) => UpdateSelection();

    private void ClearResults()
    {
        foreach (CloudRootViewModel root in Roots)
        {
            root.SelectionChanged -= OnRootSelectionChanged;
        }

        Roots.Clear();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        long total = SelectedBytes;
        int count = Roots.Count(r => r.IsSelected);
        SelectedSizeText = ByteSizeFormatter.Format(total);
        SelectionSummary = $"{count} of {Roots.Count} folders selected · {ByteSizeFormatter.Format(total)}";
        OnPropertyChanged(nameof(SelectedBytes));
        FreeUpCommand.NotifyCanExecuteChanged();
    }

    private void UpdateShares()
    {
        long max = Roots.Count == 0 ? 0 : Roots.Max(r => r.Scan.HydratedBytes);
        foreach (CloudRootViewModel root in Roots)
        {
            root.ShareOfMax = max <= 0 ? 0 : (double)root.Scan.HydratedBytes / max;
        }
    }
}
