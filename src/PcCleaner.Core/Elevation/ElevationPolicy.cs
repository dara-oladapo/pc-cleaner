namespace PcCleaner.Core.Elevation;

/// <summary>
/// Pure allowlist checks shared by the non-elevated caller and the elevated side. Every check fails closed: anything
/// unusual (relative paths, "..", control characters, separators in a name) is rejected rather than cleaned up.
/// </summary>
public static class ElevationPolicy
{
    /// <summary>Registry value names and file names longer than this are refused outright.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Case-insensitive on Windows (NTFS); exact elsewhere, which only ever makes the allowlist stricter.</summary>
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Normalises an absolute path for comparison. Fails for empty, relative, control-character or ".."-bearing paths
    /// instead of resolving them, so a caller cannot reach an allowlisted root by walking out of somewhere else.
    /// </summary>
    public static bool TryNormalizeAbsolutePath(string? path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || ContainsControlChar(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string[] segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            return false;
        }

        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> is exactly one of <paramref name="allowedRoots"/>.</summary>
    public static bool IsAllowedRoot(string? path, IEnumerable<string> allowedRoots)
    {
        if (!TryNormalizeAbsolutePath(path, out string normalized))
        {
            return false;
        }

        return allowedRoots.Any(root =>
            TryNormalizeAbsolutePath(root, out string normalizedRoot)
            && string.Equals(normalized, normalizedRoot, PathComparison));
    }

    /// <summary>
    /// Returns, in allowlist order and without duplicates, each allowed root that strictly contains at least one of
    /// <paramref name="paths"/>. Paths that are invalid or outside every root are ignored.
    /// </summary>
    public static IReadOnlyList<string> FindContainingRoots(IEnumerable<string> paths, IEnumerable<string> allowedRoots)
    {
        var roots = new List<string>();
        foreach (string root in allowedRoots)
        {
            if (TryNormalizeAbsolutePath(root, out string normalizedRoot)
                && !roots.Any(r => string.Equals(r, normalizedRoot, PathComparison)))
            {
                roots.Add(normalizedRoot);
            }
        }

        var normalizedPaths = new List<string>();
        foreach (string path in paths)
        {
            if (TryNormalizeAbsolutePath(path, out string normalized))
            {
                normalizedPaths.Add(normalized);
            }
        }

        return roots.Where(root => normalizedPaths.Any(p => IsStrictlyUnder(p, root))).ToList();
    }

    /// <summary>True when <paramref name="path"/> sits inside <paramref name="root"/> (not equal to it). Both must be normalised.</summary>
    public static bool IsStrictlyUnder(string path, string root)
    {
        if (path.Length <= root.Length || !path.StartsWith(root, PathComparison))
        {
            return false;
        }

        char next = path[root.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar
            || Path.EndsInDirectorySeparator(root);
    }

    /// <summary>A registry value name we are willing to move: non-empty, bounded, and free of control characters.</summary>
    public static bool IsValidRegistryValueName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength && !ContainsControlChar(name);

    /// <summary>
    /// A bare file name: no directory separators, drive or stream colons, wildcards, control characters, or dot-only
    /// names. Checked against both Windows and POSIX rules so the answer is the same on every OS.
    /// </summary>
    public static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name is "." or ".." || ContainsControlChar(name))
        {
            return false;
        }

        return name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !name.EndsWith(' ')
            && !name.EndsWith('.');
    }

    /// <summary>
    /// True when <paramref name="path"/> is a plain-named file with <paramref name="extension"/> directly inside one of
    /// <paramref name="allowedDirectories"/> (not in a subfolder of it). Returns the matched directory.
    /// </summary>
    public static bool IsFileDirectlyIn(
        string? path, string extension, IEnumerable<string> allowedDirectories, out string matchedDirectory)
    {
        matchedDirectory = string.Empty;

        if (!TryNormalizeAbsolutePath(path, out string normalized))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        string? parent = Path.GetDirectoryName(normalized);
        if (parent is null
            || !IsPlainFileName(fileName)
            || !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            || fileName.Length == extension.Length)
        {
            return false;
        }

        foreach (string dir in allowedDirectories)
        {
            if (TryNormalizeAbsolutePath(dir, out string normalizedDir) && string.Equals(parent, normalizedDir, PathComparison))
            {
                matchedDirectory = normalizedDir;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsControlChar(string value) => value.Any(char.IsControl);
}
