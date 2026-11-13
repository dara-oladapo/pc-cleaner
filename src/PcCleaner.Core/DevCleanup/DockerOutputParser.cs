using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcCleaner.Core.DevCleanup;

/// <summary>One row of <c>docker system df --format '{{json .}}'</c>.</summary>
public sealed record DockerDiskUsage(string Type, int TotalCount, int ActiveCount, long SizeBytes, long ReclaimableBytes);

/// <summary>One line of <c>wsl -l -v</c>.</summary>
public sealed record WslDistro(string Name, bool IsRunning, bool IsDefault);

/// <summary>Pure parsing of Docker and WSL CLI output. No process or file access.</summary>
public static partial class DockerOutputParser
{
    [GeneratedRegex(@"^\s*(?<n>[0-9]+(?:\.[0-9]+)?)\s*(?<u>[kKMGTP]?i?B)\b")]
    private static partial Regex SizePattern();

    [GeneratedRegex(@"Total(?: reclaimed space)?:\s*(?<size>[0-9.]+\s*[kKMGTP]?i?B)", RegexOptions.IgnoreCase)]
    private static partial Regex ReclaimedPattern();

    [GeneratedRegex(@"^\s*Reclaimable:\s*(?<size>[0-9.]+\s*[kKMGTP]?i?B)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BuilderReclaimablePattern();

    /// <summary>
    /// Parses Docker's human sizes ("13.5GB", "1.922GB (99%)", "512kB", "0B"). Docker uses decimal units
    /// (go-units HumanSize), so 1 GB here is 10^9 bytes; binary suffixes (GiB) are honoured if present.
    /// </summary>
    public static long? ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match = SizePattern().Match(text);
        if (!match.Success
            || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        string unit = match.Groups["u"].Value;
        bool binary = unit.Contains('i', StringComparison.Ordinal);
        double step = binary ? 1024 : 1000;
        int power = char.ToUpperInvariant(unit[0]) switch
        {
            'K' => 1,
            'M' => 2,
            'G' => 3,
            'T' => 4,
            'P' => 5,
            _ => 0,
        };

        return (long)Math.Round(value * Math.Pow(step, power));
    }

    /// <summary>Parses the JSON-lines output of <c>docker system df --format '{{json .}}'</c>. Bad lines are skipped.</summary>
    public static IReadOnlyList<DockerDiskUsage> ParseSystemDf(string output)
    {
        var rows = new List<DockerDiskUsage>();
        foreach (string line in Lines(output))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                string type = GetString(root, "Type") ?? string.Empty;
                rows.Add(new DockerDiskUsage(
                    type,
                    ParseInt(GetString(root, "TotalCount")),
                    ParseInt(GetString(root, "Active")),
                    ParseSize(GetString(root, "Size")) ?? 0,
                    ParseSize(GetString(root, "Reclaimable")) ?? 0));
            }
            catch (JsonException)
            {
                // Not one of ours; leave it out rather than guess.
            }
        }

        return rows;
    }

    /// <summary>The "Reclaimable:" total printed at the end of <c>docker builder du</c>.</summary>
    public static long? ParseBuilderDuReclaimable(string output)
    {
        Match match = BuilderReclaimablePattern().Match(output);
        return match.Success ? ParseSize(match.Groups["size"].Value) : null;
    }

    /// <summary>
    /// The space a prune reports freeing: "Total reclaimed space: 1.2GB" (image/container/volume prune) or
    /// "Total: 1.2GB" (builder prune). Null when the tool did not say.
    /// </summary>
    public static long? ParseReclaimed(string output)
    {
        Match match = ReclaimedPattern().Match(output);
        return match.Success ? ParseSize(match.Groups["size"].Value) : null;
    }

    /// <summary>Parses <c>wsl -l -v</c>: a header row, then "[*] NAME STATE VERSION" per distro.</summary>
    public static IReadOnlyList<WslDistro> ParseWslList(string output)
    {
        var distros = new List<WslDistro>();
        foreach (string raw in Lines(output))
        {
            string line = raw.Trim();
            bool isDefault = line.StartsWith('*');
            if (isDefault)
            {
                line = line[1..].Trim();
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || string.Equals(parts[0], "NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Names cannot contain spaces in WSL; state and version are the last two columns.
            string state = parts[^2];
            distros.Add(new WslDistro(parts[0], string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase), isDefault));
        }

        return distros;
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
