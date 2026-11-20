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
    /// The usual places developers keep code, under the home folder: Visual Studio's <c>source/repos</c>,
    /// <c>repos</c>, <c>src</c>, <c>code</c>, <c>Projects</c>, <c>Dev</c>, GitHub Desktop's
    /// <c>Documents/GitHub</c>, and on macOS <c>Developer</c>. Only folders that exist are returned; users add
    /// anything else in Settings.
    /// </summary>
    public static IReadOnlyList<string> DefaultRoots() => DefaultRoots(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst(),
        Directory.Exists);

    /// <summary>Testable form of <see cref="DefaultRoots()"/>.</summary>
    public static IReadOnlyList<string> DefaultRoots(string home, bool isMac, Func<string, bool> exists)
    {
        var candidates = new List<string>
        {
            Path.Combine(home, "source", "repos"),
            Path.Combine(home, "repos"),
            Path.Combine(home, "src"),
            Path.Combine(home, "code"),
            Path.Combine(home, "Projects"),
            Path.Combine(home, "Dev"),
            Path.Combine(home, "Documents", "GitHub"),
        };

        if (isMac)
        {
            candidates.Add(Path.Combine(home, "Developer"));
        }

        // Case-insensitive file systems (Windows, macOS default) would otherwise list ~/Dev and ~/dev twice.
        return candidates.Where(exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
