namespace PcCleaner.Core.Models;

/// <summary>What happened when one file was asked to go online-only.</summary>
public enum CloudReleaseStatus
{
    /// <summary>The provider was asked to release the file's local copy.</summary>
    Requested,

    /// <summary>The file was left alone, because a safety rule no longer held when it was checked again.</summary>
    Skipped,

    /// <summary>The platform call failed. The file is unchanged.</summary>
    Failed,
}

/// <summary>The outcome of one platform release call.</summary>
/// <param name="Path">The file.</param>
/// <param name="Status">What happened.</param>
/// <param name="AllocatedBytesBefore">On-disk size measured just before the request. 0 unless requested.</param>
/// <param name="SkipReason">Why the file was skipped. <see cref="CloudIneligibility.None"/> otherwise.</param>
/// <param name="Error">The platform error for a failure.</param>
public sealed record CloudReleaseAttempt(
    string Path,
    CloudReleaseStatus Status,
    long AllocatedBytesBefore,
    CloudIneligibility SkipReason = CloudIneligibility.None,
    string? Error = null);

/// <summary>One file the user chose to release, with the root it was found in.</summary>
public sealed record CloudReleaseRequest(CloudSyncRoot Root, string Path);

/// <summary>The outcome of a free-up run.</summary>
/// <param name="BytesReclaimed">Measured: on-disk size before minus after, for files the provider has already released.</param>
/// <param name="BytesPending">On-disk size of files the provider accepted but had not released when the run stopped waiting.</param>
/// <param name="ReleasedPaths">Files whose local copy is confirmed gone.</param>
/// <param name="PendingPaths">Files marked online-only that the provider has not released yet.</param>
/// <param name="Skipped">Files left alone, with the reason.</param>
/// <param name="Failed">Files the platform refused, with the error.</param>
public sealed record CloudReleaseResult(
    long BytesReclaimed,
    long BytesPending,
    IReadOnlyList<string> ReleasedPaths,
    IReadOnlyList<string> PendingPaths,
    IReadOnlyList<CloudReleaseAttempt> Skipped,
    IReadOnlyList<CloudReleaseAttempt> Failed);
