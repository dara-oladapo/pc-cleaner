using System.Text.Json;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>One row of <c>systemctl list-unit-files</c>.</summary>
public sealed record UnitFileRow(string Unit, string State);

/// <summary>Pure parsers for the <c>systemctl</c> output this app reads. No process is started here.</summary>
public static class SystemctlOutput
{
    /// <summary>
    /// The only unit-file states the user can meaningfully toggle with <c>enable</c> / <c>disable</c>. Everything else
    /// is skipped: <c>static</c> (no [Install] section), <c>generated</c> (written at boot from other config, e.g. by
    /// systemd-xdg-autostart-generator), <c>indirect</c>, <c>alias</c>, <c>transient</c>, <c>masked</c>, <c>bad</c>
    /// and the <c>-runtime</c> variants, which <c>disable</c> does not touch.
    /// </summary>
    public static bool IsToggleableState(string state) => state is "enabled" or "disabled" or "linked";

    /// <summary>Whether <paramref name="unit"/> is a template (<c>foo@.service</c>), which can't be enabled without an instance.</summary>
    public static bool IsTemplate(string unit) => unit.Contains("@.", StringComparison.Ordinal);

    /// <summary><c>systemctl list-unit-files --output=json</c>: <c>[{"unit_file":"x.service","state":"enabled",...}]</c>.</summary>
    public static IReadOnlyList<UnitFileRow>? TryParseListUnitFilesJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var rows = new List<UnitFileRow>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("unit_file", out var unit) && unit.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String)
                {
                    rows.Add(new UnitFileRow(unit.GetString()!, state.GetString()!));
                }
            }

            return rows;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Plain <c>systemctl list-unit-files --no-legend</c>: <c>UNIT-FILE STATE [PRESET]</c> per line. Tolerates a header
    /// and footer in case <c>--no-legend</c> was ignored.
    /// </summary>
    public static IReadOnlyList<UnitFileRow> ParseListUnitFilesText(string text)
    {
        var rows = new List<UnitFileRow>();
        foreach (string rawLine in text.Split('\n'))
        {
            string[] parts = rawLine.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "UNIT" || !parts[0].Contains('.'))
            {
                continue;
            }

            rows.Add(new UnitFileRow(parts[0], parts[1]));
        }

        return rows;
    }

    /// <summary>
    /// <c>systemctl show -p A,B unit1 unit2 ...</c>: <c>Key=Value</c> lines, one block per unit, blocks separated by a
    /// blank line. Values can contain <c>=</c>, so only the first one splits.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseShow(string text)
    {
        var blocks = new List<IReadOnlyDictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    blocks.Add(current);
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0)
            {
                current[line[..eq]] = line[(eq + 1)..];
            }
        }

        if (current.Count > 0)
        {
            blocks.Add(current);
        }

        return blocks;
    }

    /// <summary>
    /// The command line from a <c>show -p ExecStart</c> value, which looks like
    /// <c>{ path=/usr/bin/foo ; argv[]=/usr/bin/foo --bar ; ignore_errors=no ; ... }</c>. Only the first ExecStart.
    /// </summary>
    public static string? ParseExecStartCommand(string? execStart)
    {
        if (string.IsNullOrWhiteSpace(execStart))
        {
            return null;
        }

        const string argvMarker = "argv[]=";
        int start = execStart.IndexOf(argvMarker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += argvMarker.Length;
            int end = execStart.IndexOf(" ;", start, StringComparison.Ordinal);
            string command = (end >= 0 ? execStart[start..end] : execStart[start..]).Trim();
            return command.Length > 0 ? command : null;
        }

        const string pathMarker = "path=";
        start = execStart.IndexOf(pathMarker, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += pathMarker.Length;
            int end = execStart.IndexOf(" ;", start, StringComparison.Ordinal);
            string path = (end >= 0 ? execStart[start..end] : execStart[start..]).Trim();
            return path.Length > 0 ? path : null;
        }

        return null;
    }

    /// <summary>The first line of <c>systemctl is-enabled</c> output, e.g. <c>enabled</c> or <c>disabled</c>.</summary>
    public static string ParseIsEnabled(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
}
