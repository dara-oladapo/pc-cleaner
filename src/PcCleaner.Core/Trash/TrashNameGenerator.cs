using System.Globalization;
using System.Text;

namespace PcCleaner.Core.Trash;

/// <summary>
/// Produces the names an item can take inside <c>$trash/files</c>: its own name first, then de-duplicated
/// variants (<c>report.2.pdf</c>, <c>report.3.pdf</c>, ...) for when that name is already taken.
/// </summary>
/// <remarks>
/// The name inside the trash is not what the item is restored as (restoring uses <c>Path=</c> from the info
/// file), so any unique name is correct. Keeping the extension last just means the trash UI still shows the
/// right icon and preview.
/// </remarks>
public static class TrashNameGenerator
{
    /// <summary>
    /// Linux NAME_MAX for every common file system, in bytes. The info file name is the trash name plus
    /// <see cref="TrashInfoFile.Extension"/>, so the trash name itself has to leave room for that.
    /// </summary>
    public const int MaxNameBytes = 255 - 10; // ".trashinfo".Length == 10

    /// <summary>Upper bound on candidates, so a pathological trash can't turn one move into an endless loop.</summary>
    public const int MaxAttempts = 10_000;

    /// <summary>The candidate names to try, in order. The first is the original name (shortened only if too long).</summary>
    public static IEnumerable<string> Candidates(string originalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(originalName);
        if (originalName.Contains('/') || originalName is "." or "..")
        {
            throw new ArgumentException($"\"{originalName}\" is not a single path component.", nameof(originalName));
        }

        (string stem, string extension) = Split(originalName);

        yield return Build(stem, extension, suffix: "");
        for (int n = 2; n < MaxAttempts + 1; n++)
        {
            yield return Build(stem, extension, "." + n.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Splits off the extension. A leading dot (".bashrc") is part of the name, not an extension, and an
    /// extension longer than a typical one is treated as part of the name so "a.verylongsuffixthing" doesn't
    /// become "a.2.verylongsuffixthing".
    /// </summary>
    private static (string Stem, string Extension) Split(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1 || name.Length - dot > 16)
        {
            return (name, "");
        }

        return (name[..dot], name[dot..]);
    }

    private static string Build(string stem, string extension, string suffix)
    {
        int fixedBytes = Encoding.UTF8.GetByteCount(extension) + Encoding.UTF8.GetByteCount(suffix);
        if (fixedBytes >= MaxNameBytes)
        {
            // An extension alone that doesn't fit: drop it rather than produce an unusable name.
            extension = "";
            fixedBytes = Encoding.UTF8.GetByteCount(suffix);
        }

        return TruncateUtf8(stem, MaxNameBytes - fixedBytes) + suffix + extension;
    }

    /// <summary>Shortens to at most <paramref name="maxBytes"/> UTF-8 bytes without splitting a character.</summary>
    internal static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder();
        int used = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            int size = rune.Utf8SequenceLength;
            if (used + size > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += size;
        }

        return builder.ToString();
    }
}
