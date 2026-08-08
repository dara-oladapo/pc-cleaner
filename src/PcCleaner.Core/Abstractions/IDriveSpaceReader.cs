using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Reads the capacity of the drive holding the user's profile — the figures behind the dashboard's capacity bar.</summary>
public interface IDriveSpaceReader
{
    /// <summary>Returns null when the drive can't be read (unmapped, not ready, or access denied).</summary>
    DriveSpace? Read();
}
