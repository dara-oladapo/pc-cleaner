namespace PcCleaner.Core.Startup.Linux;

/// <summary>
/// The parts of the process environment that decide which XDG autostart entries exist and which of them run. A plain
/// value so tests can build one for a temp directory instead of touching the real <c>~/.config</c>.
/// </summary>
/// <param name="UserConfigHome"><c>$XDG_CONFIG_HOME</c>, or <c>$HOME/.config</c>; null if neither is usable.</param>
/// <param name="ConfigDirs"><c>$XDG_CONFIG_DIRS</c> in priority order (most important first), default <c>/etc/xdg</c>.</param>
/// <param name="CurrentDesktops"><c>$XDG_CURRENT_DESKTOP</c> split on <c>:</c>, e.g. <c>ubuntu</c>, <c>GNOME</c>.</param>
/// <param name="LocaleCandidates">Locale suffixes to try for <c>Name[xx]</c>, most specific first.</param>
/// <param name="PathDirectories"><c>$PATH</c>, used to resolve a relative <c>TryExec</c>.</param>
public sealed record XdgEnvironment(
    string? UserConfigHome,
    IReadOnlyList<string> ConfigDirs,
    IReadOnlyList<string> CurrentDesktops,
    IReadOnlyList<string> LocaleCandidates,
    IReadOnlyList<string> PathDirectories)
{
    public const string DefaultConfigDir = "/etc/xdg";

    public string? UserAutostartDirectory => UserConfigHome is null ? null : Path.Combine(UserConfigHome, "autostart");

    public IEnumerable<string> SystemAutostartDirectories => ConfigDirs.Select(d => Path.Combine(d, "autostart"));

    public static XdgEnvironment FromProcess() => FromVariables(Environment.GetEnvironmentVariable);

    public static XdgEnvironment FromVariables(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        // The basedir spec: a relative path in any of these variables is invalid and must be ignored.
        string? configHome = Absolute(getVariable("XDG_CONFIG_HOME"));
        if (configHome is null && Absolute(getVariable("HOME")) is { } home)
        {
            configHome = Path.Combine(home, ".config");
        }

        var configDirs = SplitAbsolute(getVariable("XDG_CONFIG_DIRS"), ':');
        if (configDirs.Count == 0)
        {
            configDirs = [DefaultConfigDir];
        }

        var desktops = (getVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? locale = FirstNonEmpty(getVariable("LC_ALL"), getVariable("LC_MESSAGES"), getVariable("LANG"));

        return new XdgEnvironment(
            configHome,
            configDirs,
            desktops,
            LocaleCandidatesFor(locale),
            SplitAbsolute(getVariable("PATH"), Path.PathSeparator));
    }

    /// <summary>
    /// The desktop-entry spec's matching order for a POSIX locale <c>lang_COUNTRY.ENCODING@MODIFIER</c>:
    /// <c>lang_COUNTRY@MODIFIER</c>, <c>lang_COUNTRY</c>, <c>lang@MODIFIER</c>, <c>lang</c>. The encoding is ignored.
    /// <c>C</c> and <c>POSIX</c> have no translations.
    /// </summary>
    public static IReadOnlyList<string> LocaleCandidatesFor(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return [];
        }

        string rest = locale.Trim();
        string? modifier = null;
        int at = rest.IndexOf('@');
        if (at >= 0)
        {
            modifier = rest[(at + 1)..];
            rest = rest[..at];
        }

        int dot = rest.IndexOf('.');
        if (dot >= 0)
        {
            rest = rest[..dot];
        }

        string? country = null;
        int underscore = rest.IndexOf('_');
        if (underscore >= 0)
        {
            country = rest[(underscore + 1)..];
            rest = rest[..underscore];
        }

        string lang = rest;
        if (lang.Length == 0 || lang is "C" or "POSIX")
        {
            return [];
        }

        modifier = string.IsNullOrEmpty(modifier) ? null : modifier;
        country = string.IsNullOrEmpty(country) ? null : country;

        var candidates = new List<string>();
        if (country is not null && modifier is not null)
        {
            candidates.Add($"{lang}_{country}@{modifier}");
        }

        if (country is not null)
        {
            candidates.Add($"{lang}_{country}");
        }

        if (modifier is not null)
        {
            candidates.Add($"{lang}@{modifier}");
        }

        candidates.Add(lang);
        return candidates;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Absolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? path : null;

    private static List<string> SplitAbsolute(string? value, char separator) =>
        (value ?? string.Empty)
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
