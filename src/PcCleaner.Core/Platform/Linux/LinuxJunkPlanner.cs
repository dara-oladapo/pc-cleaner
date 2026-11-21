using PcCleaner.Core.Models;

namespace PcCleaner.Core.Platform.Linux;

/// <summary>One place a Linux junk scan should size: a directory (cleaned of its contents) or a single file.</summary>
public sealed record LinuxJunkSource(string Path, JunkCategory Category, string Description, bool IsDirectory);

/// <summary>
/// Decides which paths a Linux junk scan sizes, and under which category. The rules for issue #10, as pure
/// logic over an <see cref="ILinuxFileSystemView"/> and a <see cref="LinuxMountTable"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every source it returns has already passed the safety checks, so the scanner only has to size them:
/// it is a directory (or, for <c>*.log</c> files, a regular file), it is not on <c>/proc</c>,
/// <c>/sys</c>, <c>/dev</c> or another pseudo file system, neither it nor any directory between it and the
/// home directory is a symlink, and no mount point lies beneath it, so walking it (and later cleaning it)
/// cannot cross onto another device. Inside each root, <c>FileSystemWalker</c> already refuses to descend
/// into symlinked directories or count symlinked files.
/// </para>
/// <para>
/// No two returned sources overlap. <see cref="JunkCategory.SystemCache"/> is "everything else" in
/// <c>XDG_CACHE_HOME</c>: one source per directory that isn't, and doesn't contain, a more specific source, so
/// the same bytes are never counted (or deleted) under two rows, and unticking "Chrome cache" really does
/// leave Chrome's cache alone.
/// </para>
/// <para>
/// System-wide caches (<c>/var/cache/apt</c>, <c>/var/cache/pacman</c>, <c>/var/cache/dnf</c>) are
/// deliberately absent: they need root, and there is no elevation story yet (#3). Listing something the
/// user can't clean is worse than leaving it out.
/// </para>
/// </remarks>
public static class LinuxJunkPlanner
{
    /// <summary>Flatpak app IDs of the browsers whose whole per-app cache (<c>~/.var/app/&lt;id&gt;/cache</c>) is browser cache.</summary>
    public static readonly IReadOnlyList<(string AppId, string Name)> FlatpakBrowsers =
    [
        ("org.mozilla.firefox", "Firefox"),
        ("com.google.Chrome", "Chrome"),
        ("org.chromium.Chromium", "Chromium"),
        ("com.brave.Browser", "Brave"),
    ];

    public static IReadOnlyList<LinuxJunkSource> Plan(
        string home,
        XdgBaseDirectories xdg,
        Func<string, string?> getEnvironmentVariable,
        ILinuxFileSystemView fileSystem,
        LinuxMountTable mounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentNullException.ThrowIfNull(xdg);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(mounts);

        home = LinuxPath.Normalize(home);
        var safety = new SafetyCheck(home, fileSystem, mounts);

        List<LinuxJunkSource> candidates = GetSpecificSources(home, xdg, getEnvironmentVariable, fileSystem);

        var planned = new List<LinuxJunkSource>();
        planned.AddRange(candidates.Where(c => safety.IsSafeRoot(c.Path)));
        planned.AddRange(PlanSystemCacheResidual(home, xdg, candidates, fileSystem, mounts, safety));
        planned.AddRange(PlanStateLogs(home, xdg.StateHome, fileSystem, safety));

        return RemoveOverlaps(planned);
    }

    /// <summary>
    /// The named rules from #10, before any existence or safety check. <c>~/.nuget/packages</c> is deliberately
    /// not here: it is the global package folder every restored project builds from, not a cache.
    /// </summary>
    public static List<LinuxJunkSource> GetSpecificSources(
        string home,
        XdgBaseDirectories xdg,
        Func<string, string?> getEnvironmentVariable,
        ILinuxFileSystemView fileSystem)
    {
        home = LinuxPath.Normalize(home);
        string cache = xdg.CacheHome;

        var sources = new List<LinuxJunkSource>
        {
            Dir(LinuxPath.Join(cache, "google-chrome"), JunkCategory.BrowserCache, "Chrome Cache"),
            Dir(LinuxPath.Join(cache, "chromium"), JunkCategory.BrowserCache, "Chromium Cache"),
            Dir(LinuxPath.Join(cache, "BraveSoftware"), JunkCategory.BrowserCache, "Brave Cache"),
        };

        // Firefox keeps a cache per profile, and only cache2 is the HTTP cache. The other per-profile entries
        // (startupCache, safebrowsing, ...) are still under XDG_CACHE_HOME, so they land in SystemCache.
        string firefoxProfiles = LinuxPath.Join(cache, "mozilla/firefox");
        foreach (LinuxDirectoryEntry profile in fileSystem.List(firefoxProfiles)
                     .Where(e => e.Kind == LinuxEntryKind.Directory)
                     .OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            sources.Add(Dir(
                LinuxPath.Join(firefoxProfiles, profile.Name + "/cache2"),
                JunkCategory.BrowserCache,
                $"Firefox Cache ({profile.Name})"));
        }

        foreach ((string appId, string name) in FlatpakBrowsers)
        {
            sources.Add(Dir(LinuxPath.Join(home, $".var/app/{appId}/cache"), JunkCategory.BrowserCache, $"{name} Cache (Flatpak)"));
        }

        sources.Add(Dir(LinuxPath.Join(cache, "pip"), JunkCategory.PackageManagerCache, "pip Cache"));
        sources.Add(Dir(LinuxPath.Join(EnvOr(getEnvironmentVariable, "npm_config_cache", LinuxPath.Join(home, ".npm")), "_cacache"),
            JunkCategory.PackageManagerCache, "npm Cache"));
        sources.Add(Dir(LinuxPath.Join(cache, "yarn"), JunkCategory.PackageManagerCache, "Yarn Cache"));
        sources.Add(Dir(LinuxPath.Join(EnvOr(getEnvironmentVariable, "CARGO_HOME", LinuxPath.Join(home, ".cargo")), "registry/cache"),
            JunkCategory.PackageManagerCache, "Cargo Registry Cache"));
        sources.Add(Dir(EnvOr(getEnvironmentVariable, "GOCACHE", LinuxPath.Join(cache, "go-build")),
            JunkCategory.PackageManagerCache, "Go Build Cache"));

        sources.Add(Dir(LinuxPath.Join(cache, "thumbnails"), JunkCategory.SystemCache, "Thumbnails"));

        // The home trash only. Per-volume $topdir/.Trash-$uid trashes belong with the freedesktop trash work (#11).
        sources.Add(Dir(LinuxPath.Join(xdg.DataHome, "Trash"), JunkCategory.TrashBin, "Trash"));

        return sources;
    }

    /// <summary>
    /// SystemCache: every directory in <c>XDG_CACHE_HOME</c> not covered by <paramref name="specific"/>. A directory
    /// that contains a specific source (or a mount point) is opened up and its other subdirectories listed instead.
    /// </summary>
    /// <remarks>
    /// Loose files are never offered, at any level. In practice the ones sitting directly in a cache directory
    /// are things like device IDs and locally generated keys (<c>deviceid</c>, <c>*.pem</c>) that apps park in
    /// <c>~/.cache</c> for convenience; deleting them is not "clearing a cache", and they are tiny anyway.
    /// </remarks>
    private static IEnumerable<LinuxJunkSource> PlanSystemCacheResidual(
        string home,
        XdgBaseDirectories xdg,
        IReadOnlyList<LinuxJunkSource> specific,
        ILinuxFileSystemView fileSystem,
        LinuxMountTable mounts,
        SafetyCheck safety)
    {
        string cacheHome = xdg.CacheHome;

        // An XDG_CACHE_HOME of $HOME, /, or a top-level directory such as /tmp would make "everything else in the
        // cache" mean real data or other users' files. The named rules still apply; the catch-all does not.
        if (LinuxPath.IsSameOrUnder(home, cacheHome) || Depth(cacheHome) < 2 || !safety.IsSafeRoot(cacheHome, allowMountsBeneath: true))
        {
            yield break;
        }

        var excluded = specific.Select(s => s.Path)
            .Concat([xdg.ConfigHome, xdg.StateHome, xdg.DataHome])
            .Concat(mounts.MountPointsBeneath(cacheHome))
            .ToList();

        var pending = new Stack<string>();
        pending.Push(cacheHome);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            foreach (LinuxDirectoryEntry entry in fileSystem.List(dir).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                string path = LinuxPath.Join(dir, entry.Name);
                if (entry.Kind != LinuxEntryKind.Directory || excluded.Any(x => LinuxPath.IsSameOrUnder(path, x)))
                {
                    continue;
                }

                if (excluded.Any(x => LinuxPath.IsStrictlyUnder(x, path)))
                {
                    pending.Push(path);
                    continue;
                }

                yield return new LinuxJunkSource(
                    path,
                    JunkCategory.SystemCache,
                    $"Cache: {LinuxPath.GetRelative(path, cacheHome)}",
                    IsDirectory: true);
            }
        }
    }

    /// <summary>
    /// SystemLogs: <c>log</c>/<c>logs</c> directories and <c>*.log</c> files at the top of <c>XDG_STATE_HOME</c> or one
    /// level down (<c>$XDG_STATE_HOME/&lt;app&gt;/logs</c>). Nothing else there: state is not cache, and deleting it
    /// loses things like shell history and editor undo.
    /// </summary>
    private static IEnumerable<LinuxJunkSource> PlanStateLogs(string home, string stateHome, ILinuxFileSystemView fileSystem, SafetyCheck safety)
    {
        if (LinuxPath.IsSameOrUnder(home, stateHome) || !safety.IsSafeRoot(stateHome, allowMountsBeneath: true))
        {
            yield break;
        }

        foreach (LinuxDirectoryEntry top in fileSystem.List(stateHome).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            string topPath = LinuxPath.Join(stateHome, top.Name);
            if (IsLogEntry(top))
            {
                if (safety.IsSafeRoot(topPath))
                {
                    yield return LogSource(topPath, top, stateHome);
                }

                continue;
            }

            if (top.Kind != LinuxEntryKind.Directory)
            {
                continue;
            }

            foreach (LinuxDirectoryEntry child in fileSystem.List(topPath).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                string childPath = LinuxPath.Join(topPath, child.Name);
                if (IsLogEntry(child) && safety.IsSafeRoot(childPath))
                {
                    yield return LogSource(childPath, child, stateHome);
                }
            }
        }
    }

    public static bool IsLogEntry(LinuxDirectoryEntry entry) => entry.Kind switch
    {
        LinuxEntryKind.Directory => entry.Name is "log" or "logs",
        LinuxEntryKind.File => entry.Name.EndsWith(".log", StringComparison.Ordinal) && entry.Name.Length > ".log".Length,
        _ => false,
    };

    private static LinuxJunkSource LogSource(string path, LinuxDirectoryEntry entry, string stateHome) =>
        new(path, JunkCategory.SystemLogs, $"Logs: {LinuxPath.GetRelative(path, stateHome)}", entry.Kind == LinuxEntryKind.Directory);

    /// <summary>Drops exact duplicates and anything nested inside an earlier source, keeping the first.</summary>
    private static List<LinuxJunkSource> RemoveOverlaps(IEnumerable<LinuxJunkSource> sources)
    {
        var kept = new List<LinuxJunkSource>();
        foreach (LinuxJunkSource source in sources)
        {
            if (kept.Any(k => LinuxPath.IsSameOrUnder(source.Path, k.Path) || LinuxPath.IsSameOrUnder(k.Path, source.Path)))
            {
                continue;
            }

            kept.Add(source);
        }

        return kept;
    }

    private static LinuxJunkSource Dir(string path, JunkCategory category, string description) =>
        new(LinuxPath.Normalize(path), category, description, IsDirectory: true);

    private static string EnvOr(Func<string, string?> getEnvironmentVariable, string name, string fallback)
    {
        string? value = getEnvironmentVariable(name);
        return LinuxPath.IsAbsolute(value) ? LinuxPath.Normalize(value!) : fallback;
    }

    private static int Depth(string path) => LinuxPath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

    private sealed class SafetyCheck(string home, ILinuxFileSystemView fileSystem, LinuxMountTable mounts)
    {
        /// <summary>
        /// A root is safe to walk and clean when it exists as a directory (or regular file), is not on a pseudo file
        /// system, is not reached through a symlink below the home directory, and has no other device mounted inside it.
        /// </summary>
        public bool IsSafeRoot(string path, bool allowMountsBeneath = false)
        {
            if (fileSystem.GetKind(path) is not (LinuxEntryKind.Directory or LinuxEntryKind.File))
            {
                return false;
            }

            if (mounts.IsPseudoFileSystem(path) || IsReachedThroughSymlink(path))
            {
                return false;
            }

            return allowMountsBeneath || mounts.MountPointsBeneath(path).Count == 0;
        }

        /// <summary>
        /// Checks the path and each parent, stopping at the home directory or a top-level directory: those are
        /// set up by the system (Fedora Silverblue's <c>/home</c> is itself a symlink), and refusing them would
        /// refuse everything.
        /// </summary>
        private bool IsReachedThroughSymlink(string path)
        {
            for (string? current = LinuxPath.Normalize(path);
                 current is not null && current != home && Depth(current) > 1;
                 current = LinuxPath.GetParent(current))
            {
                if (fileSystem.GetKind(current) == LinuxEntryKind.Symlink)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
