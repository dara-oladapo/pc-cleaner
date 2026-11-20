using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using PcCleaner.Presentation.Services;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Core.Tests;

/// <summary>Files inside a cloud drive are marked "Cloud only" and are never deleted by Large Files or Duplicates.</summary>
public class CloudOnlyTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "pccleaner-cloudonly");
    private static readonly string OneDrive = Path.Combine(Base, "OneDrive");
    private static readonly string Local = Path.Combine(Base, "Local");

    private static CloudSyncRoot Root(string path = null!) => CloudTestData.Root(path: path ?? OneDrive);

    private static CloudLocationGuard Guard(params string[] roots)
    {
        var guard = new CloudLocationGuard(
            new FakeCloudDriveScanner(roots.Select(r => Root(r)).ToList(), []), StringComparison.OrdinalIgnoreCase);
        guard.Refresh();
        return guard;
    }

    private static FileEntry Entry(string path, long bytes = 200_000_000) => new(path, bytes, CloudTestData.Now);

    // ---- the guard ----

    [Fact]
    public void Guard_FindsAFileInsideACloudFolder()
    {
        var guard = Guard(OneDrive);
        Assert.NotNull(guard.FindContaining(Path.Combine(OneDrive, "Videos", "film.mkv")));
    }

    [Fact]
    public void Guard_TreatsTheRootItselfAsInside()
    {
        Assert.True(Guard(OneDrive).IsInsideCloudDrive(OneDrive));
    }

    [Fact]
    public void Guard_DoesNotMatchASiblingFolderThatSharesAPrefix()
    {
        var guard = Guard(OneDrive);
        Assert.False(guard.IsInsideCloudDrive(OneDrive + "-old" + Path.DirectorySeparatorChar + "film.mkv"));
    }

    [Fact]
    public void Guard_IgnoresCase_WhenToldTo()
    {
        Assert.True(Guard(OneDrive).IsInsideCloudDrive(Path.Combine(OneDrive.ToUpperInvariant(), "a.bin")));
    }

    [Fact]
    public void Guard_HandlesATrailingSeparatorOnTheRoot()
    {
        Assert.True(Guard(OneDrive + Path.DirectorySeparatorChar).IsInsideCloudDrive(Path.Combine(OneDrive, "a.bin")));
    }

    [Fact]
    public void Guard_LeavesOrdinaryFilesAlone()
    {
        Assert.False(Guard(OneDrive).IsInsideCloudDrive(Path.Combine(Local, "a.bin")));
    }

    [Fact]
    public void Guard_KeepsThePreviousFolders_WhenTheyCannotBeRead()
    {
        var scanner = new ThrowingOnceScanner(Root());
        var guard = new CloudLocationGuard(scanner, StringComparison.OrdinalIgnoreCase);
        guard.Refresh();
        scanner.Fail = true;
        guard.Refresh();

        Assert.True(guard.IsInsideCloudDrive(Path.Combine(OneDrive, "a.bin")));
    }

    private sealed class ThrowingOnceScanner(CloudSyncRoot root) : ICloudDriveScanner
    {
        public bool Fail { get; set; }

        public IReadOnlyList<CloudSyncRoot> GetSyncRoots() => Fail ? throw new UnauthorizedAccessException() : [root];

        public Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
            CloudSyncRoot root, Func<CloudFileState, bool> shouldProbeInUse,
            CancellationToken cancellationToken = default, IProgress<int>? filesVisited = null) =>
            Task.FromResult<IReadOnlyList<CloudFileState>>([]);
    }

    // ---- the rows ----

    [Fact]
    public void CloudRow_CannotBeTicked()
    {
        var row = new FileEntryViewModel(Entry(Path.Combine(OneDrive, "a.bin")), Root());

        row.IsSelected = true;

        Assert.False(row.IsSelected);
        Assert.True(row.IsCloudOnly);
        Assert.False(row.CanSelect);
        Assert.Contains("OneDrive", row.CloudNote);
    }

    [Fact]
    public void OrdinaryRow_CanBeTicked()
    {
        var row = new FileEntryViewModel(Entry(Path.Combine(Local, "a.bin")));

        row.IsSelected = true;

        Assert.True(row.IsSelected);
        Assert.True(row.CanSelect);
    }

    [Fact]
    public void DuplicateGroup_NeverPreTicksACloudCopy()
    {
        var group = new DuplicateGroup("h", 10, [
            Entry(Path.Combine(Local, "keep.bin")),
            Entry(Path.Combine(OneDrive, "copy.bin")),
            Entry(Path.Combine(Local, "extra.bin"))]);
        var guard = Guard(OneDrive);

        var vm = new DuplicateGroupViewModel(group, guard.FindContaining);

        Assert.False(vm.Files[0].IsSelected);
        Assert.False(vm.Files[1].IsSelected);
        Assert.True(vm.Files[2].IsSelected);
        Assert.Equal(1, vm.CloudCopyCount);
    }

    // ---- the view model: the never-delete rule ----

    private sealed class FakeScanner(params FileEntry[] files) : ILargeFileScanner
    {
        public async IAsyncEnumerable<FileEntry> ScanAsync(
            IReadOnlyList<string> rootPaths, long minSizeBytes,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
            IProgress<int>? filesScanned = null)
        {
            foreach (FileEntry f in files)
            {
                yield return f;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class RecordingTrasher : IFileTrasher
    {
        public List<string> Trashed { get; } = [];

        public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            Trashed.AddRange(paths);
            return Task.FromResult(new TrashResult(paths, [], 0));
        }
    }

    private sealed class YesDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel") => Task.FromResult(true);

        public Task<bool> ConfirmAsync(
            string title, string message, string acceptText, string cancelText, bool isDestructive, IReadOnlyList<string>? details) =>
            Task.FromResult(true);
    }

    private sealed class MemoryStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class OneVolume : IVolumeCatalog
    {
        public VolumeSnapshot Read()
        {
            string root = Path.GetPathRoot(Base)!;
            return new VolumeSnapshot(
                [new ScanVolume(root, "Test", ScanVolumeKind.System, 100, 50)], [root], root, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class NoPicker : IFolderPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task LargeFiles_ListsACloudFileButNeverSendsItToTheRecycleBin()
    {
        Directory.CreateDirectory(Local);
        string cloudFile = Path.Combine(OneDrive, "film.mkv");
        string localFile = Path.Combine(Local, "image.iso");
        var trasher = new RecordingTrasher();
        var vm = new LargeFilesViewModel(
            new FakeScanner(Entry(cloudFile), Entry(localFile)),
            trasher,
            new YesDialogs(),
            new NoPicker(),
            new ScanVolumeSettings(new MemoryStore(), new OneVolume()),
            Guard(OneDrive));
        vm.RootPaths.Clear();
        vm.RootPaths.Add(Base);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Files.Count);
        var cloud = vm.Files.Single(f => f.Path == cloudFile);
        Assert.True(cloud.IsCloudOnly);

        // Ticking everything, the way a "select all" would, still leaves the cloud file alone.
        foreach (var f in vm.Files)
        {
            f.IsSelected = true;
        }

        await vm.CleanSelectedAsync(confirm: false);

        Assert.Equal([localFile], trasher.Trashed);
    }
}
