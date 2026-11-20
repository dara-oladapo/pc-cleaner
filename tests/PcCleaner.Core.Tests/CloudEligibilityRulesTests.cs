using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using static PcCleaner.Core.Tests.CloudTestData;

namespace PcCleaner.Core.Tests;

public class CloudEligibilityRulesTests
{
    private static readonly TimeSpan MinAge = CloudEligibilityRules.DefaultMinimumAge;

    private static CloudIneligibility Evaluate(CloudFileState file) => CloudEligibilityRules.Evaluate(file, Now, MinAge);

    [Fact]
    public void DefaultMinimumAge_IsThirtyDays() => Assert.Equal(TimeSpan.FromDays(30), CloudEligibilityRules.DefaultMinimumAge);

    [Fact]
    public void FileThatPassesEveryRule_IsEligible() => Assert.Equal(CloudIneligibility.None, Evaluate(Eligible()));

    [Fact]
    public void NotAPlaceholder_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.NotPlaceholder, Evaluate(Eligible() with { IsPlaceholder = false }));

    [Fact]
    public void ForeignReparsePoint_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.ForeignReparsePoint, Evaluate(Eligible() with { HasForeignReparsePoint = true }));

    [Fact]
    public void AlreadyOnlineOnly_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.NotHydrated, Evaluate(Eligible() with { IsHydrated = false }));

    [Fact]
    public void ZeroBytesOnDisk_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.NotHydrated, Evaluate(Eligible() with { AllocatedBytes = 0 }));

    [Fact]
    public void SystemFile_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.SystemFile, Evaluate(Eligible() with { IsSystemFile = true }));

    [Fact]
    public void Pinned_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.Pinned, Evaluate(Eligible() with { PinState = CloudPinState.Pinned }));

    [Fact]
    public void ExcludedFromSync_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.Excluded, Evaluate(Eligible() with { PinState = CloudPinState.Excluded }));

    [Theory]
    [InlineData(CloudPinState.Unspecified)]
    [InlineData(CloudPinState.Unpinned)]
    [InlineData(CloudPinState.Inherit)]
    public void UnpinnedStates_AreEligible(CloudPinState pin) =>
        Assert.Equal(CloudIneligibility.None, Evaluate(Eligible() with { PinState = pin }));

    [Theory]
    [InlineData(CloudSyncState.NotInSync)]
    [InlineData(CloudSyncState.Unknown)] // could not tell: treated as not in sync, never as "probably fine"
    public void NotKnownToBeInSync_IsNotEligible(CloudSyncState state) =>
        Assert.Equal(CloudIneligibility.NotInSync, Evaluate(Eligible() with { SyncState = state }));

    [Fact]
    public void LocallyModifiedBytes_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.LocallyModified, Evaluate(Eligible() with { ModifiedBytes = 1 }));

    [Fact]
    public void Transferring_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.Transferring, Evaluate(Eligible() with { IsTransferring = true }));

    [Fact]
    public void InUse_IsNotEligible() =>
        Assert.Equal(CloudIneligibility.InUse, Evaluate(Eligible() with { IsInUse = true }));

    [Fact]
    public void InUse_IsIgnoredByTheProbeGate() =>
        Assert.Equal(CloudIneligibility.None, CloudEligibilityRules.EvaluateIgnoringInUse(Eligible() with { IsInUse = true }, Now, MinAge));

    [Theory]
    [InlineData(-31, true)]
    [InlineData(-30, true)]
    [InlineData(-29, false)]
    [InlineData(0, false)]
    [InlineData(3, false)] // a future timestamp is never "old"
    public void Age_UsesTheThreshold(int daysOffset, bool eligible)
    {
        DateTime time = Now.AddDays(daysOffset);
        var file = Eligible() with { LastAccessUtc = time, LastWriteUtc = time };
        Assert.Equal(eligible ? CloudIneligibility.None : CloudIneligibility.RecentlyUsed, Evaluate(file));
    }

    [Fact]
    public void RecentAccess_KeepsAnOldFile()
    {
        // Downloaded files often keep the cloud's old write time; being opened yesterday still counts.
        var file = Eligible() with { LastWriteUtc = Now.AddYears(-3), LastAccessUtc = Now.AddDays(-1) };
        Assert.Equal(CloudIneligibility.RecentlyUsed, Evaluate(file));
    }

    [Fact]
    public void RecentWrite_KeepsAFileWithAnOldAccessTime()
    {
        // Volumes with last-access updates turned off report a stale access time.
        var file = Eligible() with { LastAccessUtc = Now.AddYears(-3), LastWriteUtc = Now.AddDays(-2) };
        Assert.Equal(CloudIneligibility.RecentlyUsed, Evaluate(file));
    }

    [Fact]
    public void CustomMinimumAge_IsHonoured()
    {
        var file = Eligible() with { LastAccessUtc = Now.AddDays(-10), LastWriteUtc = Now.AddDays(-10) };
        Assert.Equal(CloudIneligibility.None, CloudEligibilityRules.Evaluate(file, Now, TimeSpan.FromDays(7)));
        Assert.Equal(CloudIneligibility.RecentlyUsed, CloudEligibilityRules.Evaluate(file, Now, TimeSpan.FromDays(30)));
    }

    [Fact]
    public void PinnedAndNotInSync_ReportsPinnedFirst() =>
        Assert.Equal(CloudIneligibility.Pinned, Evaluate(Eligible() with { PinState = CloudPinState.Pinned, SyncState = CloudSyncState.NotInSync }));

    [Fact]
    public void EveryReason_HasADescription()
    {
        foreach (CloudIneligibility reason in Enum.GetValues<CloudIneligibility>())
        {
            Assert.NotEqual(reason.ToString(), CloudEligibilityRules.Describe(reason));
        }
    }
}
