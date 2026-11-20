using System.Text.RegularExpressions;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>A toggleable systemd user service.</summary>
/// <param name="Unit">The unit name, e.g. <c>syncthing.service</c>.</param>
/// <param name="Description">The unit's <c>Description=</c>, or the unit name without <c>.service</c>.</param>
/// <param name="Command">The first <c>ExecStart</c> command line, else the unit file path, else the unit name.</param>
/// <param name="Program">The program <c>ExecStart</c> runs, for matching against autostart entries.</param>
public sealed record SystemdUserUnit(string Unit, string Description, string Command, string? Program, bool IsEnabled);

/// <summary>
/// Lists and toggles systemd <em>user</em> services through <c>systemctl --user</c>. Enablement is left entirely to
/// systemctl: the rules (symlink farms under <c>*.wants</c>, <c>WantedBy=</c>, aliases, presets, drop-ins) are not
/// reimplemented here, because getting them subtly wrong would break the user's session.
/// </summary>
public sealed partial class SystemdUserUnits(ISystemctlRunner runner)
{
    private const int ShowBatchSize = 64;

    private readonly ISystemctlRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>Why the last <see cref="ListAsync"/> found nothing because systemctl failed, e.g. no user bus. Null on success.</summary>
    public string? LastListError { get; private set; }

    public async Task<IReadOnlyList<SystemdUserUnit>> ListAsync(CancellationToken cancellationToken = default)
    {
        LastListError = null;

        IReadOnlyList<UnitFileRow>? rows = null;
        var json = await _runner.RunAsync(
            ["--user", "list-unit-files", "--type=service", "--no-legend", "--no-pager", "--output=json"],
            cancellationToken).ConfigureAwait(false);
        if (json.Succeeded)
        {
            rows = SystemctlOutput.TryParseListUnitFilesJson(json.StandardOutput);
        }

        if (rows is null)
        {
            // systemd older than --output=json support (it rejects the option), or output we couldn't read as JSON.
            var text = await _runner.RunAsync(
                ["--user", "list-unit-files", "--type=service", "--no-legend", "--no-pager"],
                cancellationToken).ConfigureAwait(false);
            if (!text.Succeeded)
            {
                // No systemd, or no user manager (containers, some WSL setups): there are simply no units to show.
                LastListError = FirstLine(text.StandardError) ?? FirstLine(json.StandardError) ?? "systemctl --user failed.";
                return [];
            }

            rows = SystemctlOutput.ParseListUnitFilesText(text.StandardOutput);
        }

        var candidates = rows
            .Where(r => r.Unit.EndsWith(".service", StringComparison.Ordinal)
                && !SystemctlOutput.IsTemplate(r.Unit)
                && SystemctlOutput.IsToggleableState(r.State)
                && IsValidUnitName(r.Unit))
            .GroupBy(r => r.Unit, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var details = await ShowAsync(candidates.Select(c => c.Unit).ToList(), cancellationToken).ConfigureAwait(false);

        return candidates.Select(row =>
        {
            details.TryGetValue(row.Unit, out var props);
            string? description = props?.GetValueOrDefault("Description");
            string? command = SystemctlOutput.ParseExecStartCommand(props?.GetValueOrDefault("ExecStart"));
            string? fragment = props?.GetValueOrDefault("FragmentPath");

            return new SystemdUserUnit(
                row.Unit,
                string.IsNullOrWhiteSpace(description) ? Stem(row.Unit) : description,
                command ?? (string.IsNullOrWhiteSpace(fragment) ? row.Unit : fragment),
                DesktopEntryRules.GetProgram(command),
                row.State == "enabled");
        }).ToList();
    }

    /// <summary>
    /// <c>systemctl --user enable|disable -- unit</c>, then confirms the result with <c>is-enabled</c>, because a unit
    /// enabled globally (<c>/etc/systemd/user</c>) or by a preset can't be switched off from the user's scope and
    /// systemctl would otherwise report success.
    /// </summary>
    public async Task SetEnabledAsync(string unit, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsValidUnitName(unit) || !unit.EndsWith(".service", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{unit}' is not a systemd service name.", nameof(unit));
        }

        var result = await _runner.RunAsync(
            ["--user", "--no-pager", enabled ? "enable" : "disable", "--", unit], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                FirstLine(result.StandardError) ?? $"systemctl --user {(enabled ? "enable" : "disable")} {unit} failed (exit code {result.ExitCode}).");
        }

        var check = await _runner.RunAsync(["--user", "is-enabled", "--", unit], cancellationToken).ConfigureAwait(false);
        if (!check.Started)
        {
            return;
        }

        string state = SystemctlOutput.ParseIsEnabled(check.StandardOutput);
        if (!enabled && state == "enabled")
        {
            throw new InvalidOperationException(
                $"{unit} is still enabled, probably for all users (/etc/systemd/user). Turning it off there needs root: sudo systemctl --global disable {unit}");
        }

        if (enabled && state is not ("enabled" or "enabled-runtime" or "static" or "indirect" or "generated" or "alias"))
        {
            throw new InvalidOperationException($"systemctl reports {unit} as '{state}' after enabling it.");
        }
    }

    /// <summary>Unit names are restricted to ASCII letters, digits and <c>:-_.\@</c>; anything else is not passed on.</summary>
    public static bool IsValidUnitName(string unit) => unit.Length is > 0 and <= 255 && UnitNamePattern().IsMatch(unit);

    public static string Stem(string unit) =>
        unit.EndsWith(".service", StringComparison.Ordinal) ? unit[..^".service".Length] : unit;

    private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> ShowAsync(
        IReadOnlyList<string> units, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        for (int i = 0; i < units.Count; i += ShowBatchSize)
        {
            var batch = units.Skip(i).Take(ShowBatchSize).ToList();
            var arguments = new List<string> { "--user", "show", "--no-pager", "-p", "Id,Description,ExecStart,FragmentPath", "--" };
            arguments.AddRange(batch);

            var result = await _runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                continue; // Names alone are still a usable list.
            }

            foreach (var block in SystemctlOutput.ParseShow(result.StandardOutput))
            {
                if (block.TryGetValue("Id", out string? id) && id.Length > 0)
                {
                    details[id] = block;
                }
            }
        }

        return details;
    }

    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);

    [GeneratedRegex(@"^[A-Za-z0-9:_.\\@-]+$")]
    private static partial Regex UnitNamePattern();
}
