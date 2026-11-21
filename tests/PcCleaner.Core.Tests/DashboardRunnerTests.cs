using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;
using PcCleaner.Presentation.Services;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Core.Tests;

public class DashboardRunnerTests
{
    private sealed class FakeDialogs(bool answer) : IDialogService
    {
        public int Asked { get; private set; }

        public IReadOnlyList<string>? LastDetails { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel")
        {
            Asked++;
            return Task.FromResult(answer);
        }

        public Task<bool> ConfirmAsync(
            string title, string message, string acceptText, string cancelText, bool isDestructive, IReadOnlyList<string>? details)
        {
            Asked++;
            LastDetails = details;
            return Task.FromResult(answer);
        }
    }

    /// <summary>A tool that records what was done to it.</summary>
    private sealed class FakeTool
    {
        public int Scans { get; private set; }

        public int Cleans { get; private set; }

        public List<bool> CleanAskedToConfirm { get; } = [];

        public int Selected { get; set; } = 1;

        public Exception? ScanThrows { get; set; }

        public Exception? CleanThrows { get; set; }

        public Action? OnScan { get; set; }

        public DashboardToolViewModel Build(string title, bool canClean = true) => new()
        {
            Route = title.ToLowerInvariant(),
            Title = title,
            Caption = "Deleted permanently",
            CanClean = canClean,
            Scan = () =>
            {
                Scans++;
                OnScan?.Invoke();
                return ScanThrows is null ? Task.CompletedTask : Task.FromException(ScanThrows);
            },
            Clean = confirm =>
            {
                Cleans++;
                CleanAskedToConfirm.Add(confirm);
                return CleanThrows is null ? Task.CompletedTask : Task.FromException(CleanThrows);
            },
            SelectedBytes = () => Selected * 1024L,
            SelectedCount = () => Selected,
        };
    }

    [Fact]
    public async Task ScanOnly_ScansEveryTool_AndCleansNothing()
    {
        var dialogs = new FakeDialogs(answer: true);
        FakeTool a = new(), b = new();

        DashboardRunResult result = await new DashboardRunner(dialogs).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanOnly, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.Scanned, result.Outcome);
        Assert.Equal((1, 1), (a.Scans, b.Scans));
        Assert.Equal((0, 0), (a.Cleans, b.Cleans));
        Assert.Equal(0, dialogs.Asked);
    }

    [Fact]
    public async Task AutoClean_CleansWithoutAnyConfirmation()
    {
        var dialogs = new FakeDialogs(answer: false);
        FakeTool a = new(), b = new();

        DashboardRunResult result = await new DashboardRunner(dialogs).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.Cleaned, result.Outcome);
        Assert.Equal(0, dialogs.Asked);
        Assert.Equal((1, 1), (a.Cleans, b.Cleans));

        // The tools must not ask either, or "auto" would stop on the first dialog.
        Assert.Equal([false], a.CleanAskedToConfirm);
        Assert.Equal([false], b.CleanAskedToConfirm);
    }

    [Fact]
    public async Task ScanAndClean_AsksOnce_NamingEveryTool_ThenCleans()
    {
        var dialogs = new FakeDialogs(answer: true);
        FakeTool a = new(), b = new();

        DashboardRunResult result = await new DashboardRunner(dialogs).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.Cleaned, result.Outcome);
        Assert.Equal(1, dialogs.Asked);
        Assert.Equal(2, dialogs.LastDetails!.Count);
        Assert.Contains("Junk", dialogs.LastDetails[0]);
        Assert.Contains("Duplicates", dialogs.LastDetails[1]);
        Assert.Equal([false], a.CleanAskedToConfirm);
        Assert.Equal(2, result.Cleaned.Count);
    }

    [Fact]
    public async Task ScanAndClean_Declined_CleansNothing()
    {
        var dialogs = new FakeDialogs(answer: false);
        FakeTool a = new(), b = new();

        DashboardRunResult result = await new DashboardRunner(dialogs).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.Declined, result.Outcome);
        Assert.Equal((0, 0), (a.Cleans, b.Cleans));
    }

    [Fact]
    public async Task UntickedTool_IsNeitherScannedNorCleaned()
    {
        FakeTool a = new(), b = new();
        DashboardToolViewModel skipped = b.Build("Duplicates");
        skipped.IsIncluded = false;

        await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [a.Build("Junk"), skipped], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal((1, 1), (a.Scans, a.Cleans));
        Assert.Equal((0, 0), (b.Scans, b.Cleans));
    }

    [Fact]
    public async Task NothingTicked_ReportsNothingIncluded()
    {
        FakeTool a = new();
        DashboardToolViewModel tool = a.Build("Junk");
        tool.IsIncluded = false;

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [tool], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.NothingIncluded, result.Outcome);
        Assert.Equal(0, a.Scans);
    }

    [Fact]
    public async Task StoppedDuringScan_NeverCleans_EvenWhenTheToolReturnsNormally()
    {
        // The real tools swallow their own cancellation and return with partial results.
        using var cts = new CancellationTokenSource();
        FakeTool a = new() { OnScan = cts.Cancel };
        FakeTool b = new();

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndAutoClean, cts.Token);

        Assert.Equal(DashboardRunOutcome.Stopped, result.Outcome);
        Assert.Equal(0, b.Scans);
        Assert.Equal((0, 0), (a.Cleans, b.Cleans));
    }

    [Fact]
    public async Task StoppedOnTheLastTool_NeverCleans()
    {
        using var cts = new CancellationTokenSource();
        FakeTool a = new();
        FakeTool b = new() { OnScan = cts.Cancel };

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndAutoClean, cts.Token);

        Assert.Equal(DashboardRunOutcome.Stopped, result.Outcome);
        Assert.Equal((0, 0), (a.Cleans, b.Cleans));
    }

    [Fact]
    public async Task ToolThatOnlyReports_IsScannedButNeverCleaned()
    {
        FakeTool startup = new();

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [startup.Build("Startup", canClean: false)], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.NothingToClean, result.Outcome);
        Assert.Equal((1, 0), (startup.Scans, startup.Cleans));
    }

    [Fact]
    public async Task ToolWithNothingTicked_IsNotCleaned_AndNotNamedInTheConfirmation()
    {
        var dialogs = new FakeDialogs(answer: true);
        FakeTool a = new();
        FakeTool b = new() { Selected = 0 };

        await new DashboardRunner(dialogs).RunAsync(
            [a.Build("Junk"), b.Build("Large Files")], DashboardRunMode.ScanAndClean, CancellationToken.None);

        Assert.Single(dialogs.LastDetails!);
        Assert.Equal((1, 0), (a.Cleans, b.Cleans));
    }

    [Fact]
    public async Task FailedScan_IsReported_DoesNotStopOthers_AndIsNotCleanedFromStaleResults()
    {
        FakeTool a = new() { ScanThrows = new IOException("disk unplugged") };
        FakeTool b = new();

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal(DashboardRunOutcome.Cleaned, result.Outcome);
        Assert.Equal((0, 1), (a.Cleans, b.Cleans));
        string problem = Assert.Single(result.Problems);
        Assert.Contains("Junk", problem);
        Assert.Contains("disk unplugged", problem);
    }

    [Fact]
    public async Task FailedClean_IsReported_AndDoesNotStopOthers()
    {
        FakeTool a = new() { CleanThrows = new UnauthorizedAccessException("denied") };
        FakeTool b = new();

        DashboardRunResult result = await new DashboardRunner(new FakeDialogs(true)).RunAsync(
            [a.Build("Junk"), b.Build("Duplicates")], DashboardRunMode.ScanAndAutoClean, CancellationToken.None);

        Assert.Equal(1, b.Cleans);
        Assert.Contains("Junk could not be cleaned", Assert.Single(result.Problems));
        Assert.Equal("Duplicates", Assert.Single(result.Cleaned).Title);
    }

    // The junk tool is the one that deletes permanently, so its two entry points are pinned down directly.

    /// <summary>These tests never fail a clean, so elevation is never offered.</summary>
    private sealed class NoElevation : IElevationService
    {
        public bool CanElevate(StartupItem item) => false;

        public Task<ElevationResult> SetStartupItemEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(ElevationResult.Rejected);

        public IReadOnlyList<string> GetElevatableJunkRoots(IEnumerable<string> failedPaths) => [];

        public Task<ElevationResult> CleanJunkRootsAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default) =>
            Task.FromResult(ElevationResult.Rejected);
    }

    private sealed class FakeJunkScanner(params JunkItem[] items) : IJunkScanner
    {
        public async IAsyncEnumerable<JunkItem> ScanAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
            IProgress<int>? filesScanned = null)
        {
            foreach (JunkItem item in items)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class FakeJunkCleaner : IJunkCleaner
    {
        public List<string> Cleaned { get; } = [];

        public Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default)
        {
            Cleaned.AddRange(items.Select(i => i.Path));
            return Task.FromResult(new TrashResult(items.Select(i => i.Path).ToList(), [], items.Sum(i => i.SizeBytes)));
        }
    }

    private static JunkItem Junk(string path, bool selectedByDefault = true) =>
        new(path, 100, JunkCategory.TempFiles, path, IsDirectory: false) { SelectedByDefault = selectedByDefault };

    [Fact]
    public async Task Junk_CleanWithoutConfirm_NeverAsks_AndRemovesOnlyWhatIsTicked()
    {
        var dialogs = new FakeDialogs(answer: false);
        var cleaner = new FakeJunkCleaner();
        var vm = new JunkCleanupViewModel(new FakeJunkScanner(Junk("a"), Junk("big-store", selectedByDefault: false)), cleaner, dialogs, new NoElevation());
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.CleanSelectedAsync(confirm: false);

        Assert.Equal(0, dialogs.Asked);
        Assert.Equal(["a"], cleaner.Cleaned);
        Assert.Equal("big-store", Assert.Single(vm.Items).Path);
    }

    [Fact]
    public async Task Junk_CleanCommand_StillAsks_AndADeclineRemovesNothing()
    {
        var dialogs = new FakeDialogs(answer: false);
        var cleaner = new FakeJunkCleaner();
        var vm = new JunkCleanupViewModel(new FakeJunkScanner(Junk("a")), cleaner, dialogs, new NoElevation());
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Asked);
        Assert.Empty(cleaner.Cleaned);
    }

    [Fact]
    public void Part_WritesTheTickThrough_AndTheToolHasTheLastWord()
    {
        bool stored = false;
        var part = new DashboardPartViewModel("Temp Files", string.Empty, () => stored, v => stored = v, () => "1 KB");
        part.IsSelected = true;
        Assert.True(stored);

        // A tool that refuses the tick (a cloud folder that cannot be freed) leaves the box unticked.
        var refused = new DashboardPartViewModel("Read only", string.Empty, () => false, _ => { }, () => "0 B");
        refused.IsSelected = true;
        Assert.False(refused.IsSelected);
    }
}
