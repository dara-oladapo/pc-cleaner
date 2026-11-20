using Microsoft.Win32.SafeHandles;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using static PcCleaner.App.Platforms.Windows.CloudFilesNative;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Returns a file to online-only by setting its pin state to unpinned, which is what Explorer's
/// "Free up space" does. The provider then releases the local copy. Nothing is deleted.
/// </summary>
/// <remarks>
/// <para>
/// Why <c>CfSetPinState(CF_PIN_STATE_UNPINNED)</c> and not <c>CfDehydratePlaceholder</c>: the Cloud Files
/// documentation says that, unless a provider opts in to <c>AUTO_DEHYDRATION_ALLOWED</c>, the only supported
/// way for anyone but the provider to dehydrate a placeholder is to clear the pinned attribute and set the
/// unpinned one. The provider then dehydrates the file itself, asynchronously. <c>CfSetPinState</c> is the
/// documented call for that, and Microsoft says any application, not just the provider, may call it.
/// </para>
/// <para>
/// This leaves the final say with the provider. It knows whether its cloud copy is current, and it does not
/// release a file it cannot fetch again. The cost is that the space comes back when the provider gets to
/// it, not at once, so the caller measures the on-disk size afterwards instead of assuming.
/// </para>
/// <para>
/// The file is opened with no sharing for the duration of the check and the call. That open fails if another
/// program has the file open, and while it is held no one else can open the file to change it.
/// </para>
/// </remarks>
public sealed class WindowsCloudFileReleaser : ICloudFileReleaser
{
    public CloudReleaseAttempt TryRelease(string path, Func<CloudFileState, CloudIneligibility> recheck)
    {
        using PlaceholderModeScope scope = ExposePlaceholdersOnThisThread();

        using SafeFileHandle handle = OpenExclusive(path, out bool inUse);
        if (inUse)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Skipped, 0, CloudIneligibility.InUse);
        }

        if (handle.IsInvalid)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: $"Could not open the file (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
        }

        // Read everything again from this handle. The scan is minutes old; this is what is true now.
        var basic = GetBasicInfo(handle);
        uint? tag = GetReparseTag(handle);
        long? allocated = GetAllocationSize(handle);
        if (basic is null || tag is null || allocated is null)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: "Could not read the file's current state.");
        }

        CloudFileState state = WindowsCloudDriveScanner.BuildState(
            path,
            logicalBytes: 0,
            allocated.Value,
            basic.Value.LastAccessUtc,
            basic.Value.LastWriteUtc,
            basic.Value.Attributes,
            tag.Value,
            GetPlaceholderInfo(handle),
            isInUse: false);

        CloudIneligibility reason = recheck(state);
        if (reason != CloudIneligibility.None)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Skipped, 0, reason);
        }

        int hr = SetUnpinned(handle);
        return hr < 0
            ? new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: $"CfSetPinState failed (0x{hr:X8}).")
            : new CloudReleaseAttempt(path, CloudReleaseStatus.Requested, allocated.Value);
    }

    public long? GetAllocatedBytes(string path)
    {
        using PlaceholderModeScope scope = ExposePlaceholdersOnThisThread();
        using SafeFileHandle handle = OpenForMetadata(path);
        return handle.IsInvalid ? null : GetAllocationSize(handle);
    }
}
