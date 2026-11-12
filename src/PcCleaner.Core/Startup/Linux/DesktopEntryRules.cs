namespace PcCleaner.Core.Startup.Linux;

/// <summary>Why an autostart entry will not run. More than one can apply at once.</summary>
[Flags]
public enum AutostartInactiveReasons
{
    None = 0,

    /// <summary><c>Hidden=true</c>: the spec's "treat this entry as deleted".</summary>
    Hidden = 1 << 0,

    /// <summary><c>X-GNOME-Autostart-enabled=false</c>, which GNOME's own startup-apps dialog writes.</summary>
    GnomeAutostartDisabled = 1 << 1,

    /// <summary><c>OnlyShowIn</c> / <c>NotShowIn</c> exclude every desktop in <c>$XDG_CURRENT_DESKTOP</c>.</summary>
    NotShownInCurrentDesktop = 1 << 2,

    /// <summary><c>TryExec</c> names a program that is not installed.</summary>
    TryExecMissing = 1 << 3,

    /// <summary>Reasons the user switched off, and that <see cref="XdgAutostartStore.SetEnabled"/> can switch back on.</summary>
    UserToggleable = Hidden | GnomeAutostartDisabled,
}

/// <summary>Pure rules for reading an autostart <c>.desktop</c> entry: whether it runs, and what to call it.</summary>
public static class DesktopEntryRules
{
    public const string HiddenKey = "Hidden";
    public const string GnomeAutostartEnabledKey = "X-GNOME-Autostart-enabled";

    /// <summary>Marks a user-directory copy this app wrote to switch off a system entry. Only such copies are ever deleted.</summary>
    public const string ShadowMarkerKey = "X-PcCleaner-Shadow";

    public static AutostartInactiveReasons Evaluate(
        DesktopEntryFile entry, IReadOnlyList<string> currentDesktops, Func<string, bool> programExists)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(currentDesktops);
        ArgumentNullException.ThrowIfNull(programExists);

        var reasons = AutostartInactiveReasons.None;

        if (entry.GetBoolean(HiddenKey) == true)
        {
            reasons |= AutostartInactiveReasons.Hidden;
        }

        if (entry.GetBoolean(GnomeAutostartEnabledKey) == false)
        {
            reasons |= AutostartInactiveReasons.GnomeAutostartDisabled;
        }

        if (!IsShownIn(entry.GetList("OnlyShowIn"), entry.GetList("NotShowIn"), currentDesktops))
        {
            reasons |= AutostartInactiveReasons.NotShownInCurrentDesktop;
        }

        string? tryExec = entry.GetString("TryExec")?.Trim();
        if (!string.IsNullOrEmpty(tryExec) && !programExists(tryExec))
        {
            reasons |= AutostartInactiveReasons.TryExecMissing;
        }

        return reasons;
    }

    /// <summary>
    /// The spec's algorithm: walk <c>$XDG_CURRENT_DESKTOP</c> in order and let the first desktop that appears in either
    /// list decide. If none does, the entry is shown unless it has an <c>OnlyShowIn</c>. Desktop names compare
    /// case-sensitively, as the spec says.
    /// </summary>
    public static bool IsShownIn(
        IReadOnlyCollection<string> onlyShowIn, IReadOnlyCollection<string> notShowIn, IReadOnlyList<string> currentDesktops)
    {
        foreach (string desktop in currentDesktops)
        {
            if (onlyShowIn.Contains(desktop, StringComparer.Ordinal))
            {
                return true;
            }

            if (notShowIn.Contains(desktop, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return onlyShowIn.Count == 0;
    }

    /// <summary>The best <c>key[locale]</c> for <paramref name="localeCandidates"/>, falling back to the plain key.</summary>
    public static string? GetLocalizedString(DesktopEntryFile entry, string key, IReadOnlyList<string> localeCandidates)
    {
        foreach (string locale in localeCandidates)
        {
            if (entry.GetString($"{key}[{locale}]") is { Length: > 0 } value)
            {
                return value;
            }
        }

        return entry.GetString(key);
    }

    /// <summary>
    /// The program an <c>Exec=</c> line runs (its first argument, unquoted), or null. Used to recognise the same program
    /// started by both an autostart entry and a systemd unit.
    /// </summary>
    public static string? GetProgram(string? exec)
    {
        if (string.IsNullOrWhiteSpace(exec))
        {
            return null;
        }

        string s = exec.TrimStart();
        if (s[0] == '"')
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    sb.Append(s[++i]);
                }
                else if (s[i] == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(s[i]);
                }
            }

            return sb.ToString();
        }

        int end = s.IndexOfAny([' ', '\t']);
        return end < 0 ? s : s[..end];
    }

    /// <summary>
    /// A copy of a system entry that switches it off for this user: the original text with <c>Hidden=true</c> and the
    /// shadow marker added. Deterministic, so <see cref="XdgAutostartStore"/> can later tell whether a user-directory
    /// file is still exactly such a copy (and therefore safe to delete) or has been edited since.
    /// </summary>
    public static string CreateShadow(string systemEntryText)
    {
        var shadow = DesktopEntryFile.Parse(systemEntryText);
        shadow.Set(HiddenKey, "true");
        shadow.Set(ShadowMarkerKey, "true");
        return shadow.ToText();
    }
}
