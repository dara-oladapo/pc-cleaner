using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using static PcCleaner.App.Platforms.Windows.CloudFilesNative;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Finds the current user's Cloud Files sync roots (OneDrive, Dropbox, Box, iCloud for Windows, Synology
/// Drive…) and reports the downloaded placeholder files in them. Read-only.
/// </summary>
/// <remarks>
/// Sync roots are read from <c>HKLM\…\Explorer\SyncRootManager</c>, which is where every Cloud Files provider
/// registers. The WinRT <c>StorageProviderSyncRootManager.GetCurrentSyncRoots()</c> returns the same list,
/// but the registry needs no WinRT activation and behaves the same packaged or unpackaged.
/// Google Drive for desktop does not use Cloud Files (it mounts its own virtual drive), so it is not listed.
/// </remarks>
public sealed class WindowsCloudDriveScanner : ICloudDriveScanner
{
    private const string SyncRootManagerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";
    private const int MinProgressIntervalMs = 120;

    public IReadOnlyList<CloudSyncRoot> GetSyncRoots()
    {
        var roots = new List<CloudSyncRoot>();
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null)
        {
            return roots;
        }

        using RegistryKey? manager = Registry.LocalMachine.OpenSubKey(SyncRootManagerKey);
        if (manager is null)
        {
            return roots;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in manager.GetSubKeyNames())
        {
            if (!CloudProviderClassifier.IsCloudStorage(id))
            {
                continue;
            }

            using RegistryKey? rootKey = manager.OpenSubKey(id);
            using RegistryKey? users = rootKey?.OpenSubKey("UserSyncRoots");
            if (users?.GetValue(sid) is not string path || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!Directory.Exists(fullPath) || !seenPaths.Add(fullPath))
            {
                continue;
            }

            // DisplayNameResource is either plain text or an indirect "@dll,-id" string; the provider's own
            // name is a better label than the raw resource reference.
            string display = rootKey!.GetValue("DisplayNameResource") as string ?? string.Empty;
            if (display.Length == 0 || display.StartsWith('@'))
            {
                display = CloudProviderClassifier.ProviderName(id);
            }

            roots.Add(new CloudSyncRoot(id, display, CloudProviderClassifier.Classify(id, display), fullPath, CanFreeUp: true));
        }

        return roots;
    }

    public Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
        CloudSyncRoot root,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        // Blocking Win32 enumeration on its own thread. The placeholder mode is per thread, so it has to be set
        // on the thread that does the work and restored before that thread goes back to the pool.
        return Task.Factory.StartNew(
            () =>
            {
                using PlaceholderModeScope scope = ExposePlaceholdersOnThisThread();
                return Walk(root.Path, shouldProbeInUse, cancellationToken, filesVisited);
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static IReadOnlyList<CloudFileState> Walk(
        string rootPath,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken,
        IProgress<int>? filesVisited)
    {
        var results = new List<CloudFileState>();
        var pending = new Stack<string>();
        pending.Push(rootPath);
        int visited = 0;
        Stopwatch? clock = filesVisited is null ? null : Stopwatch.StartNew();

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();

            using SafeFindHandle find = FindFirstFileExW(
                Path.Combine(dir, "*"), FindExInfoBasic, out Win32FindData data, FindExSearchNameMatch, IntPtr.Zero, FindFirstExLargeFetch);
            if (find.IsInvalid)
            {
                continue;
            }

            do
            {
                if (data.FileName is "." or ".." || string.IsNullOrEmpty(data.FileName))
                {
                    continue;
                }

                string fullPath = Path.Combine(dir, data.FileName);
                bool isReparse = (data.Attributes & FileAttributeReparsePoint) != 0;
                bool isCloudReparse = isReparse && IsCloudReparseTag(data.Reserved0);

                if ((data.Attributes & FileAttributeDirectory) != 0)
                {
                    // Cloud folders are reparse points too. Any other reparse point (junction, symlink, mount
                    // point) is never followed, so the walk cannot leave the sync root.
                    if (!isReparse || isCloudReparse)
                    {
                        pending.Push(fullPath);
                    }

                    continue;
                }

                visited++;
                if (clock is not null && clock.ElapsedMilliseconds >= MinProgressIntervalMs)
                {
                    filesVisited!.Report(visited);
                    clock.Restart();
                }

                // Only cloud placeholders can be released. Plain files (not yet uploaded) and links are skipped
                // without being opened.
                if (!isCloudReparse)
                {
                    continue;
                }

                // Dehydrated files take no space. Skipping them here also means they are never opened.
                if ((data.Attributes & (FileAttributeRecallOnDataAccess | FileAttributeOffline)) != 0)
                {
                    continue;
                }

                CloudFileState? state = Describe(fullPath, data);
                if (state is null)
                {
                    continue;
                }

                if (shouldProbeInUse(state))
                {
                    state = state with { IsInUse = IsOpenElsewhere(fullPath) };
                }

                results.Add(state);
            }
            while (FindNextFileW(find, out data));
        }

        filesVisited?.Report(visited);
        return results;
    }

    private static CloudFileState? Describe(string path, Win32FindData data)
    {
        using SafeFileHandle handle = OpenForMetadata(path);
        if (handle.IsInvalid)
        {
            return null;
        }

        long? allocated = GetAllocationSize(handle);
        if (allocated is null)
        {
            return null;
        }

        return BuildState(
            path,
            data.Length,
            allocated.Value,
            data.LastAccessTime.ToUtc(),
            data.LastWriteTime.ToUtc(),
            data.Attributes,
            data.Reserved0,
            GetPlaceholderInfo(handle),
            isInUse: false);
    }

    /// <summary>Shared with <see cref="WindowsCloudFileReleaser"/>, so the scan and the release judge the same facts the same way.</summary>
    internal static CloudFileState BuildState(
        string path,
        long logicalBytes,
        long allocatedBytes,
        DateTime lastAccessUtc,
        DateTime lastWriteUtc,
        uint attributes,
        uint reparseTag,
        PlaceholderInfo? placeholder,
        bool isInUse)
    {
        bool isReparse = (attributes & FileAttributeReparsePoint) != 0;
        bool isCloud = isReparse && IsCloudReparseTag(reparseTag);

        CloudPinState pin = placeholder?.PinState ?? CloudPinState.Unspecified;

        // The attribute and the cfapi state should agree. If either says "keep on this device", it stays.
        if ((attributes & FileAttributePinned) != 0)
        {
            pin = CloudPinState.Pinned;
        }

        return new CloudFileState(
            Path: path,
            LogicalBytes: logicalBytes,
            AllocatedBytes: allocatedBytes,
            LastAccessUtc: lastAccessUtc,
            LastWriteUtc: lastWriteUtc,
            IsPlaceholder: isCloud && placeholder is not null,
            IsHydrated: (attributes & (FileAttributeRecallOnDataAccess | FileAttributeOffline)) == 0,
            HasForeignReparsePoint: isReparse && !isCloud,
            SyncState: placeholder?.SyncState ?? CloudSyncState.Unknown,
            ModifiedBytes: placeholder?.ModifiedBytes ?? 0,
            PinState: pin,
            IsInUse: isInUse,
            IsTransferring: false,
            IsSystemFile: (attributes & FileAttributeSystem) != 0);
    }
}
