using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class JunkSafetyRulesTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MinAge = JunkSafetyRules.DefaultMinimumAge;
    private static readonly DateTime Old = Now.AddDays(-30);

    [Fact]
    public void DefaultMinimumAge_IsTwoDays() => Assert.Equal(TimeSpan.FromDays(2), JunkSafetyRules.DefaultMinimumAge);

    [Theory]
    [InlineData(-3, true)]
    [InlineData(-2, true)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(5, false)] // a future timestamp is never "old"
    public void IsOldEnough_ComparesAgainstThreshold(int daysOffset, bool expected)
    {
        DateTime written = Now.AddDays(daysOffset);
        Assert.Equal(expected, JunkSafetyRules.IsOldEnough(written, written, Now, MinAge));
    }

    [Fact]
    public void IsOldEnough_UsesNewerOfWriteAndCreation()
    {
        // An archive extracted an hour ago keeps a years-old write time; it is still in use.
        Assert.False(JunkSafetyRules.IsOldEnough(lastWriteUtc: Old, creationUtc: Now.AddHours(-1), Now, MinAge));
        Assert.False(JunkSafetyRules.IsOldEnough(lastWriteUtc: Now.AddHours(-1), creationUtc: Old, Now, MinAge));
    }

    [Theory]
    [InlineData("ext4.vhdx")]
    [InlineData("disk.VHD")]
    [InlineData("checkpoint.avhdx")]
    [InlineData("vm.VMDK")]
    [InlineData("vm.vdi")]
    [InlineData("vm.qcow2")]
    [InlineData("install.iso")]
    [InlineData("boot.wim")]
    public void ShouldSkipFile_VirtualDisks_AlwaysSkipped(string name)
    {
        Assert.True(JunkSafetyRules.ShouldSkipFile(name, inTempRoot: true, Old, Old, Now, MinAge));
        Assert.True(JunkSafetyRules.ShouldSkipFile(name, inTempRoot: false, Old, Old, Now, MinAge));
    }

    [Fact]
    public void ShouldSkipFile_RecentFile_SkippedOnlyInTempRoots()
    {
        DateTime recent = Now.AddHours(-1);
        Assert.True(JunkSafetyRules.ShouldSkipFile("a.tmp", inTempRoot: true, recent, recent, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipFile("a.tmp", inTempRoot: false, recent, recent, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipFile("a.tmp", inTempRoot: true, Old, Old, Now, MinAge));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("Claude")]
    public void ShouldSkipDirectory_ClaudeDirectlyUnderTemp_Skipped(string name)
    {
        Assert.True(JunkSafetyRules.ShouldSkipDirectory(name, inTempRoot: true, isDirectChildOfRoot: true, Old, Old, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipDirectory(name, inTempRoot: true, isDirectChildOfRoot: false, Old, Old, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipDirectory(name, inTempRoot: false, isDirectChildOfRoot: true, Old, Old, Now, MinAge));
    }

    [Fact]
    public void ShouldSkipDirectory_RecentDirectory_SkippedOnlyInTempRoots()
    {
        DateTime recent = Now.AddHours(-1);
        Assert.True(JunkSafetyRules.ShouldSkipDirectory("x", inTempRoot: true, isDirectChildOfRoot: false, recent, recent, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipDirectory("x", inTempRoot: false, isDirectChildOfRoot: false, recent, recent, Now, MinAge));
        Assert.False(JunkSafetyRules.ShouldSkipDirectory("x", inTempRoot: true, isDirectChildOfRoot: false, Old, Old, Now, MinAge));
    }
}
