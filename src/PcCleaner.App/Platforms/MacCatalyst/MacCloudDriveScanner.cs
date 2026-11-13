using Foundation;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>
/// Finds iCloud Drive and the File Provider folders under ~/Library/CloudStorage, and reports the downloaded
/// files in them. Read-only.
/// </summary>
/// <remarks>
/// <para>
/// iCloud Drive can be freed: <c>NSFileManager.EvictUbiquitousItem</c> is the public API for it (see
/// <see cref="MacCloudFileReleaser"/>).
/// </para>
/// <para>
/// Third-party File Provider domains (OneDrive, Google Drive, Dropbox, Box…) are listed read-only. macOS
/// offers no public API for one app to evict another provider's files: <c>NSFileProviderManager.EvictItem</c>
/// is only available to the provider that owns the domain, and <c>evictUbiquitousItemAtURL</c> is
/// documented for iCloud items. Finder's "Remove Download" goes through private interfaces. Rather than
/// rely on behaviour Apple does not document, these roots show their sizes and point the user to the
/// provider's own command.
/// </para>
/// </remarks>
public sealed class MacCloudDriveScanner : ICloudDriveScanner
{
    private const string ThirdPartyHint =
        "macOS lets only the provider free up its own files. In Finder, right-click the folder and choose Remove Download, or use the provider's Free up space.";

    private static readonly NSString[] ResourceKeys =
    [
        NSUrl.IsUbiquitousItemKey,
        NSUrl.UbiquitousItemIsUploadedKey,
        NSUrl.UbiquitousItemIsUploadingKey,
        NSUrl.UbiquitousItemDownloadingStatusKey,
        NSUrl.TotalFileAllocatedSizeKey,
        NSUrl.ContentAccessDateKey,
        NSUrl.ContentModificationDateKey,
    ];

    public IReadOnlyList<CloudSyncRoot> GetSyncRoots()
    {
        var roots = new List<CloudSyncRoot>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Only the user's iCloud Drive folder. The other containers in Mobile Documents belong to apps that
        // may expect their data to be local.
        string iCloud = Path.Combine(home, "Library", "Mobile Documents", "com~apple~CloudDocs");
        if (Directory.Exists(iCloud))
        {
            roots.Add(new CloudSyncRoot("com~apple~CloudDocs", "iCloud Drive", CloudProvider.ICloudDrive, iCloud, CanFreeUp: true));
        }

        string cloudStorage = Path.Combine(home, "Library", "CloudStorage");
        if (Directory.Exists(cloudStorage))
        {
            IEnumerable<string> domains;
            try
            {
                domains = Directory.GetDirectories(cloudStorage);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                domains = [];
            }

            foreach (string domain in domains.Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(domain);
                roots.Add(new CloudSyncRoot(
                    name,
                    name.Replace('-', ' '),
                    CloudProviderClassifier.Classify(name, name),
                    domain,
                    CanFreeUp: false,
                    ReadOnlyReason: ThirdPartyHint));
            }
        }

        return roots;
    }

    public Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
        CloudSyncRoot root,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        return Task.Run<IReadOnlyList<CloudFileState>>(() =>
        {
            var results = new List<CloudFileState>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // symlinks are never followed or reported
                ReturnSpecialDirectories = false,
            };

            int visited = 0;
            foreach (string path in Directory.EnumerateFiles(root.Path, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited % 500 == 0)
                {
                    filesVisited?.Report(visited);
                }

                CloudFileState? state = Describe(path);
                if (state is not null && state.IsHydrated)
                {
                    results.Add(state);
                }
            }

            filesVisited?.Report(visited);
            return results;
        }, cancellationToken);
    }

    /// <summary>Reads a file's iCloud state. Shared with <see cref="MacCloudFileReleaser"/>.</summary>
    internal static CloudFileState? Describe(string path)
    {
        // Old-style placeholders for files that are not downloaded. They hold no content.
        if (path.EndsWith(".icloud", StringComparison.Ordinal))
        {
            return null;
        }

        using NSUrl url = NSUrl.FromFilename(path);
        NSDictionary? values = url.GetResourceValues(ResourceKeys, out NSError? error);
        if (error is not null || values is null)
        {
            return null;
        }

        bool isUbiquitous = Bool(values, NSUrl.IsUbiquitousItemKey);
        bool isUploaded = Bool(values, NSUrl.UbiquitousItemIsUploadedKey);
        bool isUploading = Bool(values, NSUrl.UbiquitousItemIsUploadingKey);
        string? status = (values[NSUrl.UbiquitousItemDownloadingStatusKey] as NSString)?.ToString();
        long allocated = (values[NSUrl.TotalFileAllocatedSizeKey] as NSNumber)?.Int64Value ?? 0;

        // "Current" means the local copy is the latest version. "Downloaded" means a newer version exists in
        // the cloud; such a file is local but not in sync, so it is reported and then kept.
        bool isCurrent = status == NSUrl.UbiquitousItemDownloadingStatusCurrent.ToString();
        bool isDownloaded = isCurrent || status == NSUrl.UbiquitousItemDownloadingStatusDownloaded.ToString();

        long logical;
        try
        {
            logical = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new CloudFileState(
            Path: path,
            LogicalBytes: logical,
            AllocatedBytes: allocated,
            LastAccessUtc: Date(values, NSUrl.ContentAccessDateKey),
            LastWriteUtc: Date(values, NSUrl.ContentModificationDateKey),
            IsPlaceholder: isUbiquitous,
            IsHydrated: isDownloaded && allocated > 0,
            HasForeignReparsePoint: false,
            SyncState: isUploaded && isCurrent ? CloudSyncState.InSync : CloudSyncState.NotInSync,
            ModifiedBytes: 0,

            // macOS has no public "keep downloaded" flag to read, so nothing reports as pinned here.
            PinState: CloudPinState.Unspecified,

            // There is no reliable public way to ask whether another process has a file open on macOS. The
            // other rules (uploaded, current, not used for N days) carry the weight.
            IsInUse: false,
            IsTransferring: isUploading,
            IsSystemFile: Path.GetFileName(path) == ".DS_Store");
    }

    private static bool Bool(NSDictionary values, NSString key) => (values[key] as NSNumber)?.BoolValue ?? false;

    // A missing date reads as MaxValue, so a file whose age cannot be read is never old enough to release.
    private static DateTime Date(NSDictionary values, NSString key) =>
        values[key] is NSDate date ? ((DateTime)date).ToUniversalTime() : DateTime.MaxValue;
}
