using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Trash;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// Moves items to the freedesktop.org Trash (#11), so they show up in, and restore from, the desktop's own
/// Trash (Files/Nautilus, Dolphin, Thunar, <c>gio trash --restore</c>).
/// </summary>
/// <remarks>
/// The algorithm lives in <see cref="FreedesktopFileTrasher"/> in Core, where it is unit-tested on every OS;
/// this class only supplies the Linux syscalls (<see cref="LinuxTrashPlatform"/>) and the home trash location.
/// Anything it can't move is reported in <see cref="TrashResult.FailedPaths"/> and left exactly where it was —
/// never deleted, never copied: the trash is the undo.
/// </remarks>
public sealed class LinuxFileTrasher : IFileTrasher
{
    private readonly IFileTrasher? _inner;

    public LinuxFileTrasher()
    {
        if (OperatingSystem.IsLinux())
        {
            string? homeTrash = TrashDirectorySelector.HomeTrashDirectory(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
                    ? home
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            _inner = new FreedesktopFileTrasher(new LinuxTrashPlatform(), homeTrash);
        }
    }

    public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (_inner is null)
        {
            // This front end can be launched on Windows/macOS for development; there the libc calls don't exist.
            const string reason = "The freedesktop Trash is only available on Linux.";
            return Task.FromResult(new TrashResult([], paths.Select(p => (p, reason)).ToList(), 0));
        }

        return _inner.MoveToTrashAsync(paths, cancellationToken);
    }
}
