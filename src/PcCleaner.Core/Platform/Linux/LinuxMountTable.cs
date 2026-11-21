using System.Text;

namespace PcCleaner.Core.Platform.Linux;

public sealed record LinuxMount(string MountPoint, string FileSystemType);

/// <summary>
/// The mount table, parsed from the text of <c>/proc/self/mountinfo</c>. Used to keep a junk walk on the
/// device it started on and out of pseudo-filesystems.
/// </summary>
/// <remarks>
/// <para>
/// This stands in for comparing <c>st_dev</c> per directory. Getting <c>st_dev</c> from .NET needs a libc
/// <c>stat</c> P/Invoke, and <c>struct stat</c>'s layout differs between x64, arm64, and 32-bit ABIs, so a
/// hand-written struct is a crash or a silent misread waiting to happen. A new device only ever starts at a
/// mount point, and every mount point is listed here, so "is there a mount point beneath this root" answers
/// the same question with no native code and no new dependency.
/// </para>
/// <para>
/// One knowingly accepted difference: btrfs subvolumes report their own <c>st_dev</c> without being separate
/// mounts. They are the same file system, so crossing into one is not the hazard this guards against.
/// </para>
/// </remarks>
public sealed class LinuxMountTable
{
    /// <summary>Kernel and virtual file systems. Nothing on these is junk, and some are unbounded or have side effects when read.</summary>
    public static readonly IReadOnlySet<string> PseudoFileSystemTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "proc", "sysfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "securityfs", "debugfs", "tracefs", "pstore",
        "bpf", "configfs", "fusectl", "mqueue", "hugetlbfs", "autofs", "binfmt_misc", "efivarfs", "nsfs",
        "rpc_pipefs", "selinuxfs",
    };

    /// <summary>Top-level trees that only ever hold pseudo-filesystems, refused by path even if the mount table is odd.</summary>
    public static readonly IReadOnlyList<string> PseudoRoots = ["/proc", "/sys", "/dev"];

    public LinuxMountTable(IEnumerable<LinuxMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        Mounts = mounts.Select(m => m with { MountPoint = LinuxPath.Normalize(m.MountPoint) }).ToList();
    }

    public IReadOnlyList<LinuxMount> Mounts { get; }

    /// <summary>
    /// Parses <c>/proc/self/mountinfo</c>: field 5 is the mount point (with <c>\040</c>-style octal escapes), and the
    /// file system type is the first field after the lone <c>-</c> separator. Malformed lines are skipped.
    /// </summary>
    public static LinuxMountTable ParseMountInfo(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var mounts = new List<LinuxMount>();
        foreach (string line in text.Split('\n'))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int separator = Array.IndexOf(fields, "-");
            if (fields.Length < 5 || separator < 6 || separator + 1 >= fields.Length)
            {
                continue;
            }

            mounts.Add(new LinuxMount(Unescape(fields[4]), fields[separator + 1]));
        }

        return new LinuxMountTable(mounts);
    }

    /// <summary>The mount that holds <paramref name="path"/>: the last-listed one with the longest matching mount point.</summary>
    public LinuxMount? FindContaining(string path)
    {
        LinuxMount? best = null;
        foreach (LinuxMount mount in Mounts)
        {
            // ">=" so a later mount stacked on the same point (which hides the earlier one) wins.
            if (LinuxPath.IsSameOrUnder(path, mount.MountPoint) &&
                (best is null || mount.MountPoint.Length >= best.MountPoint.Length))
            {
                best = mount;
            }
        }

        return best;
    }

    /// <summary>Mount points strictly beneath <paramref name="root"/>. A walk from the root would cross onto another device at each one.</summary>
    public IReadOnlyList<string> MountPointsBeneath(string root) =>
        Mounts.Select(m => m.MountPoint)
              .Where(mp => LinuxPath.IsStrictlyUnder(mp, root))
              .Distinct(StringComparer.Ordinal)
              .ToList();

    /// <summary>True when <paramref name="path"/> is under <c>/proc</c>, <c>/sys</c>, <c>/dev</c>, or on a pseudo file system.</summary>
    public bool IsPseudoFileSystem(string path)
    {
        if (PseudoRoots.Any(root => LinuxPath.IsSameOrUnder(path, root)))
        {
            return true;
        }

        LinuxMount? mount = FindContaining(path);
        return mount is not null && PseudoFileSystemTypes.Contains(mount.FileSystemType);
    }

    private static string Unescape(string field)
    {
        if (!field.Contains('\\'))
        {
            return field;
        }

        var builder = new StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            // The kernel only escapes space, tab, newline and backslash this way, so each escape is one ASCII char.
            if (field[i] == '\\' && i + 3 < field.Length &&IsOctal(field[i + 1]) && IsOctal(field[i + 2]) && IsOctal(field[i + 3]))
            {
                builder.Append((char)(((field[i + 1] - '0') * 64) + ((field[i + 2] - '0') * 8) + (field[i + 3] - '0')));
                i += 3;
            }
            else
            {
                builder.Append(field[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';
}
