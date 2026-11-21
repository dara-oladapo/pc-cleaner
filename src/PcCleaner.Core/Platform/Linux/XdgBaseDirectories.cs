namespace PcCleaner.Core.Platform.Linux;

/// <summary>
/// The four XDG base directories a junk scan cares about, resolved per the XDG Base Directory Specification.
/// </summary>
/// <remarks>
/// Pure: the environment and home directory are passed in, so the fallback rules can be tested without a real
/// Linux machine. Paths always use <c>/</c>, whatever OS runs the tests.
/// </remarks>
public sealed record XdgBaseDirectories(string CacheHome, string ConfigHome, string StateHome, string DataHome)
{
    public const string CacheHomeVariable = "XDG_CACHE_HOME";
    public const string ConfigHomeVariable = "XDG_CONFIG_HOME";
    public const string StateHomeVariable = "XDG_STATE_HOME";
    public const string DataHomeVariable = "XDG_DATA_HOME";

    /// <summary>
    /// Reads each <c>XDG_*_HOME</c> variable and falls back to the spec's default under <paramref name="home"/>
    /// when it is unset, empty, or relative. The spec says a relative path is invalid and must be ignored.
    /// </summary>
    public static XdgBaseDirectories Resolve(Func<string, string?> getEnvironmentVariable, string home)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        string normalizedHome = LinuxPath.Normalize(home);

        return new XdgBaseDirectories(
            CacheHome: Pick(getEnvironmentVariable(CacheHomeVariable), normalizedHome, ".cache"),
            ConfigHome: Pick(getEnvironmentVariable(ConfigHomeVariable), normalizedHome, ".config"),
            StateHome: Pick(getEnvironmentVariable(StateHomeVariable), normalizedHome, ".local/state"),
            DataHome: Pick(getEnvironmentVariable(DataHomeVariable), normalizedHome, ".local/share"));
    }

    private static string Pick(string? value, string home, string defaultRelative) =>
        LinuxPath.IsAbsolute(value) ? LinuxPath.Normalize(value!) : LinuxPath.Join(home, defaultRelative);
}
