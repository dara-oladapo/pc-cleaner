namespace PcCleaner.Core.Elevation;

public enum ElevationStatus
{
    /// <summary>The elevated operation ran and did everything it was asked to.</summary>
    Succeeded,

    /// <summary>The elevated operation ran but some targets could still not be changed (e.g. files in use).</summary>
    PartiallySucceeded,

    /// <summary>The user declined the OS elevation prompt (UAC "No", or Cancel on the macOS password dialog).</summary>
    Cancelled,

    /// <summary>The request was refused before anything ran because a target is not on the allowlist.</summary>
    Rejected,

    /// <summary>The elevated operation could not be started or failed outright.</summary>
    Failed,
}

public sealed record ElevationResult(ElevationStatus Status)
{
    public static ElevationResult Succeeded { get; } = new(ElevationStatus.Succeeded);

    public static ElevationResult PartiallySucceeded { get; } = new(ElevationStatus.PartiallySucceeded);

    public static ElevationResult Cancelled { get; } = new(ElevationStatus.Cancelled);

    public static ElevationResult Rejected { get; } = new(ElevationStatus.Rejected);

    public static ElevationResult Failed { get; } = new(ElevationStatus.Failed);
}

/// <summary>Process exit codes the elevated helper returns, and how the caller reads them back.</summary>
/// <remarks>
/// The non-zero codes are distinctive ("PC" = 0x5043 in the high word) so a crash, a CLR/Win32 failure code, or some
/// other process's small exit code is never mistaken for Partial or Rejected — anything unrecognised reads as Failed.
/// </remarks>
public static class ElevationExitCodes
{
    public const int Success = 0;
    public const int Failed = 0x50430001;
    public const int Rejected = 0x50430002;
    public const int Partial = 0x50430003;

    public static ElevationStatus ToStatus(int exitCode) => exitCode switch
    {
        Success => ElevationStatus.Succeeded,
        Partial => ElevationStatus.PartiallySucceeded,
        Rejected => ElevationStatus.Rejected,
        _ => ElevationStatus.Failed,
    };
}
