using PcCleaner.Core.Utilities;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

public enum DashboardRunMode
{
    /// <summary>Reads every included tool. Deletes nothing.</summary>
    ScanOnly,

    /// <summary>Scans, shows one combined confirmation, then cleans what is ticked.</summary>
    ScanAndClean,

    /// <summary>Scans, then cleans what is ticked without asking. The only path in the app with no confirmation.</summary>
    ScanAndAutoClean,
}

public enum DashboardRunOutcome
{
    NothingIncluded,
    Scanned,
    NothingToClean,
    Declined,
    Cleaned,
    Stopped,
}

/// <param name="Label">The tool being worked on.</param>
/// <param name="Finished">How many tools this phase has finished.</param>
/// <param name="Total">How many tools this phase covers.</param>
/// <param name="IsCleaning">False while scanning, true while cleaning.</param>
public sealed record DashboardRunProgress(string Label, int Finished, int Total, bool IsCleaning);

/// <param name="Problems">One sentence per tool that could not be scanned or cleaned.</param>
/// <param name="Cleaned">The tools whose clean ran to the end.</param>
public sealed record DashboardRunResult(
    DashboardRunOutcome Outcome,
    IReadOnlyList<string> Problems,
    IReadOnlyList<DashboardToolViewModel> Cleaned);

/// <summary>
/// Runs the dashboard's three actions over whichever tools are ticked.
/// </summary>
/// <remarks>
/// The rules that keep this safe live here, where they can be tested without a UI:
/// a tool that is not ticked is neither scanned nor cleaned; a stopped scan never goes on to clean; a tool
/// whose scan failed is not cleaned from stale results; a declined confirmation removes nothing; and one
/// tool failing never stops the others.
/// </remarks>
public sealed class DashboardRunner(IDialogService dialogs)
{
    public async Task<DashboardRunResult> RunAsync(
        IReadOnlyList<DashboardToolViewModel> tools,
        DashboardRunMode mode,
        CancellationToken cancellationToken,
        Action<DashboardRunProgress>? onProgress = null)
    {
        var problems = new List<string>();
        var included = tools.Where(t => t.IsIncluded).ToList();
        if (included.Count == 0)
        {
            return new DashboardRunResult(DashboardRunOutcome.NothingIncluded, problems, []);
        }

        foreach (DashboardToolViewModel tool in included)
        {
            tool.IsStepDone = false;
        }

        // Sequential, not parallel. These are disk-bound walks over the same drives; running them at once
        // makes every one slower, and it would make "N of M finished" meaningless.
        var failedScans = new HashSet<DashboardToolViewModel>();
        int finished = 0;
        foreach (DashboardToolViewModel tool in included)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new DashboardRunResult(DashboardRunOutcome.Stopped, problems, []);
            }

            onProgress?.Invoke(new DashboardRunProgress(tool.Title, finished, included.Count, IsCleaning: false));

            try
            {
                await tool.Scan();
            }
            catch (OperationCanceledException)
            {
                return new DashboardRunResult(DashboardRunOutcome.Stopped, problems, []);
            }
            catch (Exception ex)
            {
                failedScans.Add(tool);
                problems.Add($"{tool.Title} could not be scanned: {ex.Message}");
            }

            tool.Rebuild();
            tool.IsStepDone = true;
            finished++;
            onProgress?.Invoke(new DashboardRunProgress(tool.Title, finished, included.Count, IsCleaning: false));
        }

        // Each tool catches its own cancellation and returns normally with partial results, so the token is
        // checked again here. Partial results must never be cleaned.
        if (cancellationToken.IsCancellationRequested)
        {
            return new DashboardRunResult(DashboardRunOutcome.Stopped, problems, []);
        }

        if (mode == DashboardRunMode.ScanOnly)
        {
            return new DashboardRunResult(DashboardRunOutcome.Scanned, problems, []);
        }

        var cleanable = included
            .Where(t => t.CanClean && !failedScans.Contains(t) && t.SelectedCount() > 0)
            .ToList();
        if (cleanable.Count == 0)
        {
            return new DashboardRunResult(DashboardRunOutcome.NothingToClean, problems, []);
        }

        if (mode == DashboardRunMode.ScanAndClean && !await ConfirmAsync(cleanable))
        {
            return new DashboardRunResult(DashboardRunOutcome.Declined, problems, []);
        }

        var cleaned = new List<DashboardToolViewModel>();
        finished = 0;
        foreach (DashboardToolViewModel tool in cleanable)
        {
            onProgress?.Invoke(new DashboardRunProgress(tool.Title, finished, cleanable.Count, IsCleaning: true));

            try
            {
                await tool.Clean(false);
                cleaned.Add(tool);
            }
            catch (Exception ex)
            {
                problems.Add($"{tool.Title} could not be cleaned: {ex.Message}");
            }

            tool.Rebuild();
            finished++;
            onProgress?.Invoke(new DashboardRunProgress(tool.Title, finished, cleanable.Count, IsCleaning: true));
        }

        return new DashboardRunResult(DashboardRunOutcome.Cleaned, problems, cleaned);
    }

    private Task<bool> ConfirmAsync(IReadOnlyList<DashboardToolViewModel> cleanable)
    {
        long bytes = cleanable.Sum(t => t.SelectedBytes());

        // Every tool gets its own line, with what will happen to its items. This replaces the confirmation
        // each tool would have shown, so nothing a tool's own dialog says may be left out.
        var details = cleanable
            .Select(t => $"{ByteSizeFormatter.Format(t.SelectedBytes()),9}   {t.Title}: {t.Caption.ToLowerInvariant()}")
            .ToList();

        return dialogs.ConfirmAsync(
            title: $"Clean {ByteSizeFormatter.Format(bytes)} from {cleanable.Count} tool(s)?",
            message: "Everything ticked on the dashboard is cleaned, one tool after another. Anything deleted permanently cannot be undone.",
            acceptText: "Clean now",
            cancelText: "Cancel",
            isDestructive: true,
            details: details);
    }
}
