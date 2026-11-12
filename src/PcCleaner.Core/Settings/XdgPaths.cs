namespace PcCleaner.Core.Settings;

/// <summary>Resolves per-user directories by the XDG Base Directory spec, as Linux desktops expect.</summary>
public static class XdgPaths
{
    /// <summary>The folder (under the config home) every PC Cleaner settings file lives in.</summary>
    public const string AppFolderName = "pc-cleaner";

    /// <summary><c>$XDG_CONFIG_HOME/pc-cleaner/settings.json</c>, falling back to <c>~/.config/pc-cleaner/settings.json</c>.</summary>
    public static string SettingsFile(Func<string, string?>? getEnvironmentVariable = null)
        => Path.Combine(ConfigHome(getEnvironmentVariable), AppFolderName, "settings.json");

    /// <summary>
    /// <c>$XDG_CONFIG_HOME</c> when it is set to an absolute path, otherwise <c>$HOME/.config</c>. The spec says a
    /// relative value must be ignored, so it is — rather than resolving it against whatever the working directory is.
    /// </summary>
    public static string ConfigHome(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        string? configHome = getEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome) && Path.IsPathRooted(configHome))
        {
            return configHome;
        }

        string? home = getEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(home, ".config");
    }
}
