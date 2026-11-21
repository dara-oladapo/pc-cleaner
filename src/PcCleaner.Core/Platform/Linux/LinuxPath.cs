namespace PcCleaner.Core.Platform.Linux;

/// <summary>
/// String-only helpers for Linux paths. They never touch the file system and always use <c>/</c> and ordinal
/// (case-sensitive) comparison, so the Linux rules behave identically when their tests run on Windows.
/// </summary>
public static class LinuxPath
{
    public static bool IsAbsolute(string? path) => !string.IsNullOrEmpty(path) && path[0] == '/';

    /// <summary>Collapses repeated slashes and drops a trailing one. <c>/</c> stays <c>/</c>. Does not resolve <c>..</c>.</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string joined = string.Join('/', segments);
        return path.StartsWith('/') ? "/" + joined : joined;
    }

    public static string Join(string parent, string child)
    {
        string left = Normalize(parent);
        string right = Normalize(child).TrimStart('/');
        if (right.Length == 0)
        {
            return left;
        }

        return left == "/" ? "/" + right : left + "/" + right;
    }

    /// <summary>The parent directory, or <c>null</c> for <c>/</c> or a single relative segment.</summary>
    public static string? GetParent(string path)
    {
        string normalized = Normalize(path);
        int slash = normalized.LastIndexOf('/');
        if (slash < 0 || normalized == "/")
        {
            return null;
        }

        return slash == 0 ? "/" : normalized[..slash];
    }

    public static string GetFileName(string path)
    {
        string normalized = Normalize(path);
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or anywhere beneath it.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        string p = Normalize(path);
        string r = Normalize(root);
        if (r == "/")
        {
            return p.StartsWith('/');
        }

        return p == r || p.StartsWith(r + "/", StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="path"/> is strictly beneath <paramref name="root"/>.</summary>
    public static bool IsStrictlyUnder(string path, string root) =>
        IsSameOrUnder(path, root) && Normalize(path) != Normalize(root);

    /// <summary><paramref name="path"/> relative to <paramref name="root"/>, or the full path if it isn't under it.</summary>
    public static string GetRelative(string path, string root)
    {
        string p = Normalize(path);
        string r = Normalize(root);
        if (!IsStrictlyUnder(p, r))
        {
            return p;
        }

        return r == "/" ? p[1..] : p[(r.Length + 1)..];
    }
}
