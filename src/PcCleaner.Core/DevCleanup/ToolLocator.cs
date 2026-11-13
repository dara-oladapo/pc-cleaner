namespace PcCleaner.Core.DevCleanup;

/// <summary>
/// Finds a command-line tool by looking at files on disk. It never runs anything, so it is safe to call while
/// developer mode is off (for the one-time "turn developer mode on?" suggestion).
/// </summary>
public static class ToolLocator
{
    /// <summary>
    /// Places GUI apps on macOS do not have on PATH (a Finder-launched app gets a minimal PATH), where Homebrew,
    /// Docker Desktop and the Xcode tools put their binaries.
    /// </summary>
    private static readonly string[] ExtraUnixDirectories =
    [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/Applications/Docker.app/Contents/Resources/bin",
        "/usr/bin",
    ];

    /// <summary>Full path of <paramref name="name"/> (e.g. "git", "docker"), or null if it is not installed.</summary>
    public static string? Find(string name)
    {
        foreach (string dir in SearchDirectories())
        {
            foreach (string candidate in CandidateNames(name))
            {
                try
                {
                    string path = Path.Combine(dir, candidate);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
        }

        return null;
    }

    public static bool IsInstalled(string name) => Find(name) is not null;

    private static IEnumerable<string> SearchDirectories()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return dir.Trim('"');
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (string dir in ExtraUnixDirectories)
            {
                yield return dir;
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "bin");
        }
    }

    private static IEnumerable<string> CandidateNames(string name)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(name))
        {
            yield return name;
            yield break;
        }

        string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        foreach (string ext in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return name + ext.ToLowerInvariant();
        }
    }
}
