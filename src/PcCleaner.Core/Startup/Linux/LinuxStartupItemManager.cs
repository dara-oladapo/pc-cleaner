using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>
/// Linux startup items: XDG autostart entries plus systemd user services, merged into one list.
/// </summary>
/// <remarks>
/// <para>Some programs are registered with both mechanisms (GNOME components ship a <c>.desktop</c> file and a
/// matching unit, and plenty of tray apps do the same). Listing both would show one program twice, and switching off
/// one copy would leave the other starting it, so they are merged: one item, enabled if either would start it, and
/// toggling it toggles both.</para>
/// <para>Ids are <c>xdg:&lt;desktop-id&gt;</c>, <c>systemd-user:&lt;unit&gt;</c>, or for a merged item
/// <c>systemd-user:&lt;unit&gt;|xdg:&lt;desktop-id&gt;</c>. Unit names can't contain <c>|</c>, so the split is unambiguous.</para>
/// </remarks>
public sealed class LinuxStartupItemManager(XdgAutostartStore autostart, SystemdUserUnits systemd) : IStartupItemManager
{
    public const string XdgPrefix = "xdg:";
    public const string SystemdPrefix = "systemd-user:";

    // Launchers whose name says nothing about the program they launch; matching on them would merge unrelated items.
    private static readonly HashSet<string> GenericPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "env", "sh", "bash", "dash", "zsh", "fish", "flatpak", "snap", "python", "python3", "perl", "ruby", "node",
        "java", "dbus-run-session", "dbus-launch", "systemd-run", "gio", "xdg-open", "exec", "nice", "ionice",
    };

    private readonly XdgAutostartStore _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
    private readonly SystemdUserUnits _systemd = systemd ?? throw new ArgumentNullException(nameof(systemd));

    public static LinuxStartupItemManager CreateForCurrentProcess() =>
        new(new XdgAutostartStore(XdgEnvironment.FromProcess()), new SystemdUserUnits(new ProcessSystemctlRunner()));

    public async Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await Task.Run(_autostart.List, cancellationToken).ConfigureAwait(false);
        var units = await _systemd.ListAsync(cancellationToken).ConfigureAwait(false);
        return Merge(entries, units);
    }

    public async Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (unit, desktopId) = ParseId(item.Id);

        if (unit is not null)
        {
            await _systemd.SetEnabledAsync(unit, enabled, cancellationToken).ConfigureAwait(false);
        }

        if (desktopId is not null)
        {
            try
            {
                _autostart.SetEnabled(desktopId, enabled);
            }
            catch (InvalidOperationException) when (enabled && unit is not null)
            {
                // A merged item whose .desktop half can never run on this desktop (OnlyShowIn / TryExec): the unit
                // is what starts it here, and that has just been enabled.
            }
        }
    }

    /// <summary>Combines both lists, merging an autostart entry and a unit that start the same program.</summary>
    public static IReadOnlyList<StartupItem> Merge(IReadOnlyList<XdgAutostartEntry> entries, IReadOnlyList<SystemdUserUnit> units)
    {
        var items = new List<StartupItem>();
        var matchedEntries = new HashSet<XdgAutostartEntry>();

        foreach (var unit in units)
        {
            var match = entries.FirstOrDefault(e => !matchedEntries.Contains(e) && IsSameProgram(e, unit));
            if (match is null)
            {
                items.Add(new StartupItem(
                    SystemdPrefix + unit.Unit,
                    unit.Description,
                    unit.Command,
                    StartupItemSource.LinuxSystemdUser,
                    StartupItemScope.CurrentUser,
                    unit.IsEnabled));
                continue;
            }

            matchedEntries.Add(match);
            items.Add(new StartupItem(
                $"{SystemdPrefix}{unit.Unit}|{XdgPrefix}{match.DesktopId}",
                match.Name,
                match.Exec,
                StartupItemSource.LinuxXdgAutostart,
                match.Scope,
                match.IsEnabled || unit.IsEnabled));
        }

        foreach (var entry in entries.Where(e => !matchedEntries.Contains(e)))
        {
            items.Add(new StartupItem(
                XdgPrefix + entry.DesktopId,
                entry.Name,
                entry.Exec,
                StartupItemSource.LinuxXdgAutostart,
                entry.Scope,
                entry.IsEnabled));
        }

        return items;
    }

    public static (string? Unit, string? DesktopId) ParseId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        string? unit = null;
        string? desktopId = null;
        string rest = id;

        if (rest.StartsWith(SystemdPrefix, StringComparison.Ordinal))
        {
            int bar = rest.IndexOf('|');
            unit = bar < 0 ? rest[SystemdPrefix.Length..] : rest[SystemdPrefix.Length..bar];
            rest = bar < 0 ? string.Empty : rest[(bar + 1)..];
        }

        if (rest.StartsWith(XdgPrefix, StringComparison.Ordinal))
        {
            desktopId = rest[XdgPrefix.Length..];
        }
        else if (rest.Length > 0 || unit is null)
        {
            throw new ArgumentException($"'{id}' is not a Linux startup item id.", nameof(id));
        }

        return (unit, desktopId);
    }

    private static bool IsSameProgram(XdgAutostartEntry entry, SystemdUserUnit unit)
    {
        string entryStem = Path.GetFileNameWithoutExtension(entry.DesktopId);
        string unitStem = SystemdUserUnits.Stem(unit.Unit);
        if (string.Equals(entryStem, unitStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? entryProgram = ProgramName(entry.Program);
        string? unitProgram = ProgramName(unit.Program);
        if (entryProgram is null)
        {
            return false;
        }

        return string.Equals(entryProgram, unitStem, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entryProgram, unitProgram, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProgramName(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return null;
        }

        string name = program[(program.LastIndexOf('/') + 1)..];
        return name.Length == 0 || GenericPrograms.Contains(name) ? null : name;
    }
}
