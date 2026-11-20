using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Stub until #11 (freedesktop trash spec). Moves nothing; reports every path as not moved.</summary>
/// <remarks>
/// A failure result rather than an exception, so a caller that reaches it keeps its rows and tells the user
/// nothing happened. It must never fall back to a permanent delete: the trash is the undo.
/// </remarks>
public sealed class LinuxFileTrasher : IFileTrasher
{
    private const string Reason = "Moving to Trash is not implemented on Linux yet (see #11).";

    public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrashResult([], paths.Select(p => (p, Reason)).ToList(), 0));
}
