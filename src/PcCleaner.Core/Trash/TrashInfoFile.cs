using System.Globalization;
using System.Text;

namespace PcCleaner.Core.Trash;

/// <summary>
/// Formats the <c>.trashinfo</c> file defined by the freedesktop.org Trash specification 1.0.
/// Pure string work: nothing here touches the file system.
/// </summary>
/// <remarks>
/// See https://specifications.freedesktop.org/trash-spec/trashspec-1.0.html. The info file is what lets the
/// desktop's own trash UI restore an item to where it came from, so getting it byte-for-byte right is the
/// whole point of trashing instead of deleting.
/// </remarks>
public static class TrashInfoFile
{
    /// <summary>Appended to the trashed item's name to form its info file name in <c>$trash/info</c>.</summary>
    public const string Extension = ".trashinfo";

    /// <summary>
    /// The spec's DeletionDate format: local time, <c>YYYY-MM-DDThh:mm:ss</c>, no time zone, no fractional seconds.
    /// </summary>
    public const string DeletionDateFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Builds the complete info file contents.</summary>
    /// <param name="originalPath">
    /// The item's original location, unescaped: absolute for the home trash, relative to the mount's top
    /// directory for a per-mount trash. Escaped here.
    /// </param>
    /// <param name="deletionDate">When the item was trashed, in local time.</param>
    public static string Format(string originalPath, DateTime deletionDate)
    {
        ArgumentException.ThrowIfNullOrEmpty(originalPath);

        // "\n" explicitly, never Environment.NewLine: the file is read by Linux desktops, and these tests
        // also run on Windows where NewLine would silently produce CRLF.
        return "[Trash Info]\n"
            + "Path=" + EncodePath(originalPath) + "\n"
            + "DeletionDate=" + deletionDate.ToString(DeletionDateFormat, CultureInfo.InvariantCulture) + "\n";
    }

    /// <summary>
    /// Percent-encodes a path for the <c>Path=</c> key, the same way GLib's gio does
    /// (<c>g_uri_escape_string(path, "/", FALSE)</c>): the path is encoded as UTF-8 and every byte other than
    /// an RFC 3986 unreserved character (<c>A-Z a-z 0-9 - . _ ~</c>) or <c>/</c> becomes <c>%XX</c>.
    /// </summary>
    /// <remarks>
    /// The spec says "escaped in the same way as in URLs (as defined in RFC 2396)". Matching gio exactly
    /// rather than a looser reading of RFC 2396 means the files we write are the files the most widely used
    /// reader already round-trips, and every compliant reader decodes either form.
    /// </remarks>
    public static string EncodePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes = Encoding.UTF8.GetBytes(path);
        var builder = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            if (IsUnescaped(b))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Reverses <see cref="EncodePath"/> (and accepts any valid percent-encoding, upper or lower case).</summary>
    /// <exception cref="FormatException">A <c>%</c> is not followed by two hex digits.</exception>
    public static string DecodePath(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var bytes = new List<byte>(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            char c = encoded[i];
            if (c == '%')
            {
                if (i + 2 >= encoded.Length
                    || !byte.TryParse(encoded.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                {
                    throw new FormatException($"Invalid percent-escape at position {i} in \"{encoded}\".");
                }

                bytes.Add(value);
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsUnescaped(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~' or (byte)'/';
}
