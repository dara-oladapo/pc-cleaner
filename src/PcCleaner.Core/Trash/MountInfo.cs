using System.Globalization;
using System.Text;

namespace PcCleaner.Core.Trash;

/// <summary>One line of <c>/proc/self/mountinfo</c>. See proc(5).</summary>
/// <param name="MountId">Unique id of this mount (field 1). Matches <c>statx</c>'s <c>stx_mnt_id</c>.</param>
/// <param name="ParentId">Id of the parent mount (field 2).</param>
/// <param name="DeviceMajor">Major part of <c>st_dev</c> for files on this mount (field 3).</param>
/// <param name="DeviceMinor">Minor part of <c>st_dev</c> for files on this mount (field 3).</param>
/// <param name="Root">The directory within the file system that forms this mount's root (field 4, unescaped).</param>
/// <param name="MountPoint">Where it is mounted, relative to the process root (field 5, unescaped).</param>
/// <param name="MountOptions">Per-mount options such as <c>ro</c>/<c>rw</c> (field 6).</param>
/// <param name="FileSystemType">File system type, e.g. <c>ext4</c> (first field after the <c>-</c> separator).</param>
/// <param name="Source">Mount source, e.g. <c>/dev/sda1</c> (unescaped).</param>
/// <param name="SuperOptions">Per-superblock options.</param>
public sealed record MountInfoEntry(
    int MountId,
    int ParentId,
    uint DeviceMajor,
    uint DeviceMinor,
    string Root,
    string MountPoint,
    IReadOnlyList<string> MountOptions,
    string FileSystemType,
    string Source,
    IReadOnlyList<string> SuperOptions)
{
    /// <summary>
    /// True when writes through this mount are refused: either the mount itself is <c>ro</c>, or the whole
    /// file system is (a bind mount can be read-only over a writable file system, and vice versa).
    /// </summary>
    public bool IsReadOnly => MountOptions.Contains("ro") || SuperOptions.Contains("ro");
}

/// <summary>Parses <c>/proc/self/mountinfo</c> and answers "which mount is this path on?". Pure: takes the text, never reads it.</summary>
/// <remarks>
/// mountinfo rather than <c>/proc/mounts</c> because it carries the mount id (which <c>statx</c> reports for
/// any file, giving an exact answer instead of a string-prefix guess) and the device number (which lets a
/// prefix guess be checked).
/// </remarks>
public static class MountInfoParser
{
    /// <summary>Parses every well-formed line. Malformed lines are skipped rather than failing the whole table.</summary>
    public static IReadOnlyList<MountInfoEntry> Parse(string mountInfoText)
    {
        ArgumentNullException.ThrowIfNull(mountInfoText);

        var entries = new List<MountInfoEntry>();
        foreach (string line in mountInfoText.Split('\n'))
        {
            if (TryParseLine(line.TrimEnd('\r'), out MountInfoEntry? entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Parses a single mountinfo line.</summary>
    public static bool TryParseLine(string line, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MountInfoEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // Fields are separated by single spaces; spaces inside paths are escaped as \040, so a plain split is safe.
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 6 fixed fields, zero or more optional fields, "-", then 3 more.
        if (fields.Length < 9)
        {
            return false;
        }

        int separator = Array.IndexOf(fields, "-", 6);
        if (separator < 0 || fields.Length < separator + 3)
        {
            return false;
        }

        if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int mountId)
            || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentId))
        {
            return false;
        }

        string[] device = fields[2].Split(':');
        if (device.Length != 2
            || !uint.TryParse(device[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint major)
            || !uint.TryParse(device[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint minor))
        {
            return false;
        }

        entry = new MountInfoEntry(
            mountId,
            parentId,
            major,
            minor,
            Root: Unescape(fields[3]),
            MountPoint: Unescape(fields[4]),
            MountOptions: fields[5].Split(','),
            FileSystemType: Unescape(fields[separator + 1]),
            Source: Unescape(fields[separator + 2]),
            SuperOptions: fields.Length > separator + 3 ? fields[separator + 3].Split(',') : []);
        return true;
    }

    /// <summary>
    /// Undoes the kernel's octal escaping of space, tab, newline and backslash (<c>\040 \011 \012 \134</c>).
    /// Any other byte, including non-ASCII UTF-8, is written raw by the kernel and passes through.
    /// </summary>
    public static string Unescape(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!field.Contains('\\'))
        {
            return field;
        }

        // Work in bytes: an octal escape is a raw byte, and could in principle be one byte of a UTF-8 sequence.
        var bytes = new List<byte>(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length
                && IsOctal(field[i + 1]) && IsOctal(field[i + 2]) && IsOctal(field[i + 3]))
            {
                int value = ((field[i + 1] - '0') << 6) | ((field[i + 2] - '0') << 3) | (field[i + 3] - '0');
                if (value <= 0xFF)
                {
                    bytes.Add((byte)value);
                    i += 3;
                    continue;
                }
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(field[i].ToString()));
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>The entry with this mount id, or null. If ids repeat (they shouldn't), the last one wins.</summary>
    public static MountInfoEntry? FindByMountId(IReadOnlyList<MountInfoEntry> entries, int mountId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.LastOrDefault(e => e.MountId == mountId);
    }

    /// <summary>
    /// The mount whose mount point is the longest whole-component prefix of <paramref name="absolutePath"/>.
    /// When two mounts share a mount point the later one (the one stacked on top) wins, which is what the
    /// kernel resolves the path to.
    /// </summary>
    /// <remarks>
    /// A fallback for kernels without <c>STATX_MNT_ID</c> (before 5.8). It works on the path as written, so the
    /// caller must pass a path without symlinks in it and should confirm the answer by device number.
    /// </remarks>
    public static MountInfoEntry? FindContainingMount(IReadOnlyList<MountInfoEntry> entries, string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);

        MountInfoEntry? best = null;
        foreach (MountInfoEntry entry in entries)
        {
            if (IsWithin(absolutePath, entry.MountPoint) && (best is null || entry.MountPoint.Length >= best.MountPoint.Length))
            {
                best = entry;
            }
        }

        return best;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="directory"/> or inside it, comparing whole components.</summary>
    public static bool IsWithin(string path, string directory)
    {
        if (directory == "/")
        {
            return path.StartsWith('/');
        }

        string trimmed = directory.TrimEnd('/');
        return path == trimmed
            || (path.Length > trimmed.Length && path.StartsWith(trimmed, StringComparison.Ordinal) && path[trimmed.Length] == '/');
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';
}
