namespace PcCleaner.Core.DevCleanup;

/// <summary>Where and how strictly the folder-based providers (worktrees, build artifacts) look.</summary>
public sealed record DevCleanupOptions
{
    /// <summary>Folders searched for repositories and projects. Missing ones are ignored.</summary>
    public IReadOnlyList<string> Roots { get; init; } = DefaultRoots();

    /// <summary>A worktree must be idle this long (HEAD commit and top-level files) before it is offered.</summary>
    public int WorktreeIdleDays { get; init; } = 14;

    /// <summary>A project must have no source file modified this recently before its build output is offered.</summary>
    public int ArtifactIdleDays { get; init; } = 30;

    /// <summary>How many folder levels below a root are searched for repositories and projects.</summary>
    public int MaxDepth { get; init; } = 6;

    /// <summary>
    /// <c>~/Dev</c>, <c>~/source/repos</c> and <c>~/Projects</c>; on Windows also <c>C:\Dev</c> (the system
    /// drive's), a common place for code that is not under the profile. Only folders that exist are returned.
    /// </summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, "Dev"),
            Path.Combine(home, "source", "repos"),
            Path.Combine(home, "Projects"),
        };

        if (OperatingSystem.IsWindows())
        {
            string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(systemDrive))
            {
                candidates.Add(Path.Combine(systemDrive, "Dev"));
            }
        }

        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
