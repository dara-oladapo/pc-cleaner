namespace PcCleaner.Core.Elevation;

/// <summary>
/// Thrown by a platform service when an operation was refused only because it needs admin/root rights, so the UI
/// can offer to retry it elevated instead of showing the underlying access-denied error.
/// </summary>
public sealed class ElevationRequiredException : Exception
{
    public ElevationRequiredException()
        : base("This operation needs administrator permission.")
    {
    }

    public ElevationRequiredException(string message)
        : base(message)
    {
    }

    public ElevationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
