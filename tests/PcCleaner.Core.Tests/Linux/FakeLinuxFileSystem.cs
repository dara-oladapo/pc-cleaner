using PcCleaner.Core.Platform.Linux;

namespace PcCleaner.Core.Tests.Linux;

/// <summary>An in-memory tree for the Linux planner tests. Adding a path implies every parent directory.</summary>
internal sealed class FakeLinuxFileSystem : ILinuxFileSystemView
{
    private readonly Dictionary<string, LinuxEntryKind> _entries = new(StringComparer.Ordinal) { ["/"] = LinuxEntryKind.Directory };

    public FakeLinuxFileSystem Directory(params string[] paths)
    {
        foreach (string path in paths)
        {
            Add(path, LinuxEntryKind.Directory);
        }

        return this;
    }

    public FakeLinuxFileSystem File(params string[] paths)
    {
        foreach (string path in paths)
        {
            Add(path, LinuxEntryKind.File);
        }

        return this;
    }

    public FakeLinuxFileSystem Symlink(params string[] paths)
    {
        foreach (string path in paths)
        {
            Add(path, LinuxEntryKind.Symlink);
        }

        return this;
    }

    public FakeLinuxFileSystem Other(params string[] paths)
    {
        foreach (string path in paths)
        {
            Add(path, LinuxEntryKind.Other);
        }

        return this;
    }

    /// <summary>Every path the planner listed, so tests can prove it never looked somewhere.</summary>
    public List<string> Listed { get; } = [];

    public LinuxEntryKind GetKind(string path) =>
        _entries.TryGetValue(LinuxPath.Normalize(path), out LinuxEntryKind kind) ? kind : LinuxEntryKind.Missing;

    public IReadOnlyList<LinuxDirectoryEntry> List(string directory)
    {
        string dir = LinuxPath.Normalize(directory);
        Listed.Add(dir);
        if (GetKind(dir) != LinuxEntryKind.Directory)
        {
            return [];
        }

        return _entries
            .Where(e => e.Key != dir && LinuxPath.GetParent(e.Key) == dir)
            .Select(e => new LinuxDirectoryEntry(LinuxPath.GetFileName(e.Key), e.Value))
            .ToList();
    }

    private void Add(string path, LinuxEntryKind kind)
    {
        string normalized = LinuxPath.Normalize(path);
        for (string? parent = LinuxPath.GetParent(normalized); parent is not null; parent = LinuxPath.GetParent(parent))
        {
            _entries.TryAdd(parent, LinuxEntryKind.Directory);
        }

        _entries[normalized] = kind;
    }
}
