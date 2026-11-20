namespace PcCleaner.Core.Tests.Linux;

/// <summary>A fact that only means something on Linux (Unix file modes, a real systemctl). Skipped elsewhere so CI stays green.</summary>
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Linux only.";
        }
    }
}
