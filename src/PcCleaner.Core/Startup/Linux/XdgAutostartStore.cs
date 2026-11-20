using PcCleaner.Core.Models;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>One XDG autostart entry after shadowing has been resolved.</summary>
/// <param name="DesktopId">The file name, e.g. <c>org.gnome.Software.desktop</c>. A user file with the same name hides a system one.</param>
/// <param name="EffectivePath">The file that wins: the user copy if there is one, else the first system directory's.</param>
/// <param name="Scope"><see cref="StartupItemScope.CurrentUser"/> if <see cref="EffectivePath"/> is in the user directory.</param>
/// <param name="Program">The program <c>Exec=</c> runs, for matching against systemd units.</param>
public sealed record XdgAutostartEntry(
    string DesktopId,
    string EffectivePath,
    StartupItemScope Scope,
    string Name,
    string Exec,
    string? Program,
    AutostartInactiveReasons InactiveReasons)
{
    public bool IsEnabled => InactiveReasons == AutostartInactiveReasons.None;
}

/// <summary>
/// Reads and toggles XDG autostart entries (the freedesktop "Desktop Application Autostart" spec) under the directories
/// an <see cref="XdgEnvironment"/> names. Plain file I/O against those directories only, so it runs anywhere and tests
/// point it at a temp directory.
/// </summary>
/// <remarks>
/// Toggling never needs root and never deletes anything the user wrote:
/// <list type="bullet">
/// <item>Disabling a user entry sets <c>Hidden=true</c> in it, editing that one line.</item>
/// <item>Disabling a system entry writes a copy to the user directory with <c>Hidden=true</c> and a marker key.</item>
/// <item>Enabling removes <c>Hidden=true</c> (and turns <c>X-GNOME-Autostart-enabled=false</c> back on). The only file
/// ever deleted is a marked copy that is still byte-for-byte what disabling wrote, because deleting it restores
/// exactly the system entry the user had before.</item>
/// </list>
/// </remarks>
public sealed class XdgAutostartStore
{
    private readonly XdgEnvironment _environment;
    private readonly Func<string, bool> _programExists;

    public XdgAutostartStore(XdgEnvironment environment, Func<string, bool>? programExists = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _programExists = programExists ?? DefaultProgramExists;
    }

    public IReadOnlyList<XdgAutostartEntry> List()
    {
        var entries = new List<XdgAutostartEntry>();
        foreach (var (desktopId, chain) in CollectChains())
        {
            if (TryBuildEntry(desktopId, chain) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public void SetEnabled(string desktopId, bool enabled)
    {
        ValidateDesktopId(desktopId);

        string userDir = _environment.UserAutostartDirectory
            ?? throw new InvalidOperationException("Neither XDG_CONFIG_HOME nor HOME is set, so there is no user autostart directory to write to.");

        string userPath = Path.Combine(userDir, desktopId);
        string? systemPath = _environment.SystemAutostartDirectories
            .Select(d => Path.Combine(d, desktopId))
            .FirstOrDefault(File.Exists);

        bool userExists = File.Exists(userPath);
        if (!userExists && systemPath is null)
        {
            throw new FileNotFoundException($"No autostart entry named {desktopId} exists any more.", desktopId);
        }

        var effective = DesktopEntryFile.Parse(File.ReadAllText(userExists ? userPath : systemPath!));

        if (enabled)
        {
            var blocking = DesktopEntryRules.Evaluate(effective, _environment.CurrentDesktops, _programExists)
                & ~AutostartInactiveReasons.UserToggleable;
            if (blocking != AutostartInactiveReasons.None)
            {
                throw new InvalidOperationException(DescribeBlocking(blocking));
            }
        }

        if (userExists)
        {
            if (enabled && systemPath is not null && IsUntouchedShadow(userPath, systemPath))
            {
                File.Delete(userPath);
                return;
            }

            bool changed = enabled ? Enable(effective) : effective.Set(DesktopEntryRules.HiddenKey, "true");

            // A bare "[Desktop Entry]\nHidden=true" stub (some tools write these to switch off a system entry) is
            // useless once Hidden is gone: with no Exec it would still hide the system entry, just as a broken file.
            // It carries nothing of the user's, so removing it is what restores the entry.
            if (enabled && systemPath is not null && IsEmptyStub(effective))
            {
                File.Delete(userPath);
                return;
            }

            if (changed)
            {
                WriteReplacing(userPath, effective.ToText());
            }

            return;
        }

        // Only a system entry exists: anything we change goes into a user-directory copy that shadows it.
        string systemText = File.ReadAllText(systemPath!);
        string? copy;
        if (enabled)
        {
            // Switched off by its vendor (Hidden / X-GNOME-Autostart-enabled=false); switch it on for this user only.
            copy = Enable(effective) ? effective.ToText() : null;
        }
        else
        {
            copy = DesktopEntryRules.CreateShadow(systemText);
        }

        if (copy is not null)
        {
            Directory.CreateDirectory(userDir);
            WriteNew(userPath, copy, systemPath!);
        }
    }

    private static bool Enable(DesktopEntryFile entry)
    {
        bool changed = false;
        if (entry.GetBoolean(DesktopEntryRules.HiddenKey) == true)
        {
            changed |= entry.Remove(DesktopEntryRules.HiddenKey);
        }

        if (entry.GetBoolean(DesktopEntryRules.GnomeAutostartEnabledKey) == false)
        {
            changed |= entry.Set(DesktopEntryRules.GnomeAutostartEnabledKey, "true");
        }

        return changed;
    }

    private static bool IsEmptyStub(DesktopEntryFile entry) =>
        entry.GetKeys().All(k => k is DesktopEntryRules.GnomeAutostartEnabledKey or DesktopEntryRules.ShadowMarkerKey);

    /// <summary>
    /// A user file is ours to delete only if it carries the marker, is exactly what disabling the current system
    /// entry would write, and the system entry it would reveal is not itself switched off.
    /// </summary>
    private bool IsUntouchedShadow(string userPath, string systemPath)
    {
        string userText = File.ReadAllText(userPath);
        var user = DesktopEntryFile.Parse(userText);
        if (user.GetBoolean(DesktopEntryRules.ShadowMarkerKey) != true)
        {
            return false;
        }

        string systemText = File.ReadAllText(systemPath);
        if (!string.Equals(userText, DesktopEntryRules.CreateShadow(systemText), StringComparison.Ordinal))
        {
            return false;
        }

        var system = DesktopEntryFile.Parse(systemText);
        return (DesktopEntryRules.Evaluate(system, _environment.CurrentDesktops, _programExists)
            & AutostartInactiveReasons.UserToggleable) == AutostartInactiveReasons.None;
    }

    private IEnumerable<(string DesktopId, List<string> Chain)> CollectChains()
    {
        // Priority order: the user directory, then each XDG_CONFIG_DIRS entry in the order given.
        var directories = new List<string>();
        if (_environment.UserAutostartDirectory is { } user)
        {
            directories.Add(user);
        }

        directories.AddRange(_environment.SystemAutostartDirectories);

        var chains = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string directory in directories.Distinct(StringComparer.Ordinal))
        {
            IEnumerable<string> files;
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                files = Directory.EnumerateFiles(directory, "*.desktop").Order(StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string id = Path.GetFileName(file);
                if (!chains.TryGetValue(id, out var chain))
                {
                    chains[id] = chain = [];
                    order.Add(id);
                }

                chain.Add(file);
            }
        }

        return order.Select(id => (id, chains[id]));
    }

    private XdgAutostartEntry? TryBuildEntry(string desktopId, List<string> chain)
    {
        var files = new List<DesktopEntryFile>();
        foreach (string path in chain)
        {
            try
            {
                files.Add(DesktopEntryFile.Parse(File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (files.Count == 0)
                {
                    return null; // The winning file is unreadable; guessing from a file it hides would mislead.
                }
            }
        }

        var effective = files[0];
        string? type = effective.GetString("Type");
        if (type is not null && type != "Application")
        {
            return null;
        }

        // A user file may be a bare "[Desktop Entry]\nHidden=true" stub: it decides whether the entry runs, but the
        // name and command shown come from the file it hides.
        string? exec = files.Select(f => f.GetString("Exec")).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        if (exec is null)
        {
            return null;
        }

        string name = files
            .Select(f => DesktopEntryRules.GetLocalizedString(f, "Name", _environment.LocaleCandidates))
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? Path.GetFileNameWithoutExtension(desktopId);

        bool isUser = _environment.UserAutostartDirectory is { } userDir
            && string.Equals(chain[0], Path.Combine(userDir, desktopId), StringComparison.Ordinal);

        return new XdgAutostartEntry(
            desktopId,
            chain[0],
            isUser ? StartupItemScope.CurrentUser : StartupItemScope.AllUsers,
            name,
            exec,
            DesktopEntryRules.GetProgram(exec),
            DesktopEntryRules.Evaluate(effective, _environment.CurrentDesktops, _programExists));
    }

    private bool DefaultProgramExists(string program)
    {
        if (program.Contains('/'))
        {
            return Path.IsPathFullyQualified(program) && File.Exists(program);
        }

        return _environment.PathDirectories.Any(d => File.Exists(Path.Combine(d, program)));
    }

    private string DescribeBlocking(AutostartInactiveReasons blocking)
    {
        var parts = new List<string>();
        if (blocking.HasFlag(AutostartInactiveReasons.NotShownInCurrentDesktop))
        {
            string desktop = _environment.CurrentDesktops.Count > 0 ? string.Join(":", _environment.CurrentDesktops) : "unknown";
            parts.Add($"it is restricted to other desktop environments (OnlyShowIn/NotShowIn; this desktop is {desktop})");
        }

        if (blocking.HasFlag(AutostartInactiveReasons.TryExecMissing))
        {
            parts.Add("the program its TryExec names is not installed");
        }

        return $"This entry can't be enabled because {string.Join(" and ", parts)}.";
    }

    private static void ValidateDesktopId(string desktopId)
    {
        if (string.IsNullOrWhiteSpace(desktopId)
            || desktopId != Path.GetFileName(desktopId)
            || desktopId.Contains('/')
            || desktopId is "." or ".."
            || !desktopId.EndsWith(".desktop", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{desktopId}' is not an autostart desktop file name.", nameof(desktopId));
        }
    }

    /// <summary>Replaces a file via a temp file + rename in the same directory, keeping its Unix permissions.</summary>
    private static void WriteReplacing(string path, string text)
    {
        string temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, text);
            CopyUnixMode(path, temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Creates a file that must not already exist (so a racing write by the user is never overwritten).</summary>
    private static void WriteNew(string path, string text, string modeFrom)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(text);
        }

        CopyUnixMode(modeFrom, path);
    }

    private static void CopyUnixMode(string from, string to)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // Never copy group/other write: a system file's mode must not make the user's copy writable by others.
            var mode = File.GetUnixFileMode(from) & ~(UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
            File.SetUnixFileMode(to, mode | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
