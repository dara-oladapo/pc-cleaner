using System.Text;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>
/// A freedesktop.org desktop entry (<c>.desktop</c>) file, held as its original lines so that an edit changes only
/// the lines it has to. Comments, blank lines, key order, other groups, unknown keys and line endings (even mixed
/// ones) all survive a <see cref="Parse"/> / <see cref="ToText"/> round trip byte for byte.
/// </summary>
/// <remarks>
/// This matters because the files being edited belong to the user: disabling an autostart entry must flip one key,
/// not rewrite the file in this program's idea of a canonical layout.
/// </remarks>
public sealed class DesktopEntryFile
{
    public const string MainGroup = "Desktop Entry";

    // Each line keeps its own trailing '\r' if it had one; lines are joined with '\n'.
    private readonly List<string> _lines;
    private readonly bool _crlf;

    private DesktopEntryFile(List<string> lines, bool crlf)
    {
        _lines = lines;
        _crlf = crlf;
    }

    public static DesktopEntryFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new DesktopEntryFile([.. text.Split('\n')], text.Contains("\r\n", StringComparison.Ordinal));
    }

    public string ToText() => string.Join('\n', _lines);

    /// <summary>Whether the file has a <c>[group]</c> header at all.</summary>
    public bool HasGroup(string group = MainGroup) => FindGroup(group) is not null;

    /// <summary>
    /// The raw (still escaped) value of <paramref name="key"/> in <paramref name="group"/>, or null if absent.
    /// <paramref name="key"/> may carry a locale suffix, e.g. <c>Name[de]</c>. If a broken file repeats a key, the
    /// last occurrence wins, which is what GLib's key-file parser does.
    /// </summary>
    public string? GetRaw(string key, string group = MainGroup)
    {
        string? value = null;
        foreach (int index in KeyLineIndexes(group, key))
        {
            value = ValueOf(_lines[index]);
        }

        return value;
    }

    /// <summary>Every key in <paramref name="group"/>, in file order (repeats included).</summary>
    public IReadOnlyList<string> GetKeys(string group = MainGroup)
    {
        var keys = new List<string>();
        if (FindGroup(group) is { } range)
        {
            for (int i = range.HeaderIndex + 1; i < range.EndExclusive; i++)
            {
                if (TryGetKey(_lines[i], out string? key))
                {
                    keys.Add(key!);
                }
            }
        }

        return keys;
    }

    /// <summary>The value of a <c>string</c> / <c>localestring</c> key with escapes (\s \n \t \r \\) decoded.</summary>
    public string? GetString(string key, string group = MainGroup) =>
        GetRaw(key, group) is { } raw ? Unescape(raw) : null;

    /// <summary>A <c>boolean</c> key. Anything other than <c>true</c> / <c>false</c> (or absent) is null.</summary>
    public bool? GetBoolean(string key, string group = MainGroup) =>
        GetRaw(key, group) switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

    /// <summary>A <c>;</c>-separated list key (e.g. <c>OnlyShowIn</c>), unescaped, with empty items dropped.</summary>
    public IReadOnlyList<string> GetList(string key, string group = MainGroup)
    {
        string? raw = GetRaw(key, group);
        if (raw is null)
        {
            return [];
        }

        var items = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\\' && i + 1 < raw.Length && raw[i + 1] == ';')
            {
                current.Append(';');
                i++;
            }
            else if (c == ';')
            {
                items.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        items.Add(current.ToString());
        return items.Select(s => Unescape(s).Trim()).Where(s => s.Length > 0).ToList();
    }

    /// <summary>
    /// Sets <paramref name="key"/> to the raw value <paramref name="rawValue"/>. Existing lines for the key are edited
    /// in place (keeping their spacing around <c>=</c>); otherwise one line is inserted after the group's last key, so
    /// trailing comments and blank lines stay where they were. Creates the group at the end if it is missing.
    /// Returns whether the text changed.
    /// </summary>
    public bool Set(string key, string rawValue, string group = MainGroup)
    {
        ValidateKey(key);
        if (rawValue.Contains('\n') || rawValue.Contains('\r'))
        {
            throw new ArgumentException("A desktop entry value cannot contain a line break; escape it as \\n.", nameof(rawValue));
        }

        var indexes = KeyLineIndexes(group, key).ToList();
        if (indexes.Count > 0)
        {
            bool changed = false;
            foreach (int index in indexes)
            {
                string line = _lines[index];
                string cr = line.EndsWith('\r') ? "\r" : string.Empty;
                string content = cr.Length > 0 ? line[..^1] : line;

                int valueStart = content.IndexOf('=') + 1;
                while (valueStart < content.Length && (content[valueStart] == ' ' || content[valueStart] == '\t'))
                {
                    valueStart++;
                }

                string updated = content[..valueStart] + rawValue + cr;
                if (updated != line)
                {
                    _lines[index] = updated;
                    changed = true;
                }
            }

            return changed;
        }

        string eol = _crlf ? "\r" : string.Empty;
        string newLine = $"{key}={rawValue}{eol}";
        if (FindGroup(group) is not { } range)
        {
            // Append "[group]\nkey=value" while keeping whatever the file ended with (normally a final newline).
            bool endsWithNewline = _lines.Count > 0 && _lines[^1].Length == 0;
            if (endsWithNewline)
            {
                _lines.RemoveAt(_lines.Count - 1);
            }

            if (_lines.Count > 0)
            {
                _lines.Add(eol);
            }

            _lines.Add($"[{group}]{eol}");
            _lines.Add(newLine);
            _lines.Add(string.Empty);
            return true;
        }

        int insertAt = range.HeaderIndex + 1;
        for (int i = range.HeaderIndex + 1; i < range.EndExclusive; i++)
        {
            if (TryGetKey(_lines[i], out _))
            {
                insertAt = i + 1;
            }
        }

        // The group is the last thing in a file with no final newline: give the previous last line one.
        if (insertAt == _lines.Count && _lines[^1].Length > 0)
        {
            _lines[^1] += eol;
            _lines.Add(newLine.TrimEnd('\r'));
            return true;
        }

        _lines.Insert(insertAt, newLine);
        return true;
    }

    /// <summary>Removes every line for <paramref name="key"/> in <paramref name="group"/>. Returns whether any was removed.</summary>
    public bool Remove(string key, string group = MainGroup)
    {
        var indexes = KeyLineIndexes(group, key).ToList();
        for (int i = indexes.Count - 1; i >= 0; i--)
        {
            _lines.RemoveAt(indexes[i]);
        }

        return indexes.Count > 0;
    }

    /// <summary>Decodes the escapes the spec defines for string values: <c>\s \n \t \r \\</c>. Unknown escapes are kept.</summary>
    public static string Unescape(string raw)
    {
        if (!raw.Contains('\\'))
        {
            return raw;
        }

        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '\\' || i + 1 >= raw.Length)
            {
                sb.Append(c);
                continue;
            }

            char next = raw[++i];
            sb.Append(next switch
            {
                's' => " ",
                'n' => "\n",
                't' => "\t",
                'r' => "\r",
                '\\' => "\\",
                _ => "\\" + next,
            });
        }

        return sb.ToString();
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains('\n') || key.Contains('\r') || key.Trim() != key)
        {
            throw new ArgumentException($"'{key}' is not a valid desktop entry key.", nameof(key));
        }
    }

    private (int HeaderIndex, int EndExclusive)? FindGroup(string group)
    {
        int header = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (TryGetGroupName(_lines[i], out string? name))
            {
                if (header >= 0)
                {
                    return (header, i);
                }

                if (name == group)
                {
                    header = i;
                }
            }
        }

        return header >= 0 ? (header, _lines.Count) : null;
    }

    private IEnumerable<int> KeyLineIndexes(string group, string key)
    {
        if (FindGroup(group) is not { } range)
        {
            yield break;
        }

        for (int i = range.HeaderIndex + 1; i < range.EndExclusive; i++)
        {
            if (TryGetKey(_lines[i], out string? lineKey) && lineKey == key)
            {
                yield return i;
            }
        }
    }

    private static bool TryGetGroupName(string line, out string? name)
    {
        string trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            name = trimmed[1..^1];
            return true;
        }

        name = null;
        return false;
    }

    private static bool TryGetKey(string line, out string? key)
    {
        key = null;
        string trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '[')
        {
            return false;
        }

        int eq = line.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        key = line[..eq].Trim();
        return key.Length > 0;
    }

    private static string ValueOf(string line) =>
        line[(line.IndexOf('=') + 1)..].TrimEnd('\r').TrimStart(' ', '\t').TrimEnd(' ', '\t');
}
