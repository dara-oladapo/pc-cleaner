using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class ScanVolumePlannerTests
{
    private static readonly ScanVolume C = new(@"C:\", "Windows (C:)", ScanVolumeKind.System, 1000, 400);
    private static readonly ScanVolume D = new(@"D:\", "Data (D:)", ScanVolumeKind.Fixed, 1000, 900);
    private static readonly ScanVolume E = new(@"E:\", "Removable Disk (E:)", ScanVolumeKind.Removable, 64, 10);

    private static VolumeSnapshot WindowsSnapshot(params ScanVolume[] volumes) =>
        new(volumes, volumes.Select(v => v.RootPath).Append(@"Z:\").ToList(), @"C:\", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] UserFolders = [@"C:\Users\me\Documents", @"C:\Users\me\Downloads"];

    [Fact]
    public void Default_SystemVolumeOnly_ScansTheUserFoldersAndNothingElse()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C, D), [@"C:\"]);

        Assert.Equal(UserFolders, plan.Roots);
        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.UnavailableVolumes);
    }

    [Fact]
    public void SystemVolume_IsNeverWalkedFromItsRoot()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C), [@"C:\"]);

        Assert.DoesNotContain(@"C:\", plan.Roots);
    }

    [Fact]
    public void SelectedDataVolume_IsWalkedFromItsRoot()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C, D), [@"C:\", @"D:\"]);

        Assert.Equal([.. UserFolders, @"D:\"], plan.Roots);
    }

    [Fact]
    public void FolderOnAnUnselectedVolume_IsSkippedWithAReason()
    {
        var plan = ScanVolumePlanner.Plan([@"C:\Users\me\Documents", @"D:\Photos"], WindowsSnapshot(C, D), [@"C:\"]);

        Assert.Equal([@"C:\Users\me\Documents"], plan.Roots);
        SkippedScanRoot skipped = Assert.Single(plan.Skipped);
        Assert.Equal(@"D:\Photos", skipped.Path);
        Assert.Contains("Data (D:)", skipped.Reason);
        Assert.Contains("not selected", skipped.Reason);
    }

    [Fact]
    public void SystemVolumeDeselected_SkipsItsFolders_ButStillScansOtherSelectedVolumes()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C, D), [@"D:\"]);

        Assert.Equal([@"D:\"], plan.Roots);
        Assert.Equal(2, plan.Skipped.Count);
    }

    [Fact]
    public void FolderOnANetworkDrive_IsSkipped_NotAttributedToAnotherVolume()
    {
        var plan = ScanVolumePlanner.Plan([@"Z:\Shared"], WindowsSnapshot(C), [@"C:\"]);

        Assert.Empty(plan.Roots);
        Assert.Contains("not on a local volume", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void SelectedVolumeThatDisappeared_IsReportedAndItsFoldersSkipped_WithoutFailingTheScan()
    {
        // E: was selected on a previous run and has since been unplugged.
        var plan = ScanVolumePlanner.Plan([.. UserFolders, @"E:\Camera"], WindowsSnapshot(C, D), [@"C:\", @"E:\"]);

        Assert.Equal(UserFolders, plan.Roots);
        Assert.Equal([@"E:\"], plan.UnavailableVolumes);
        Assert.Contains("not connected", Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void FolderInsideASelectedVolume_IsNotWalkedTwice()
    {
        var plan = ScanVolumePlanner.Plan([@"C:\Users\me\Documents", @"D:\Photos"], WindowsSnapshot(C, D), [@"C:\", @"D:\"]);

        Assert.Equal([@"C:\Users\me\Documents", @"D:\"], plan.Roots);
    }

    [Fact]
    public void NestedFolders_AreCollapsedToTheOutermost()
    {
        var plan = ScanVolumePlanner.Plan([@"C:\Users\me", @"C:\Users\me\Documents"], WindowsSnapshot(C), [@"C:\"]);

        Assert.Equal([@"C:\Users\me"], plan.Roots);
    }

    [Fact]
    public void SiblingWithACommonPrefix_IsNotTreatedAsNested()
    {
        var plan = ScanVolumePlanner.Plan([@"C:\Data", @"C:\Data2"], WindowsSnapshot(C), [@"C:\"]);

        Assert.Equal([@"C:\Data", @"C:\Data2"], plan.Roots);
    }

    [Fact]
    public void Selection_IsMatchedCaseInsensitivelyOnWindows()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C, D), [@"c:\", @"d:\"]);

        Assert.Equal([.. UserFolders, @"D:\"], plan.Roots);
    }

    [Fact]
    public void Mac_UserFoldersAreOnRoot_AndAnExternalVolumeIsWalkedWhole()
    {
        var root = new ScanVolume("/", "Macintosh HD", ScanVolumeKind.System, 1000, 400);
        var backup = new ScanVolume("/Volumes/Backup", "Backup", ScanVolumeKind.External, 1000, 400);
        var snapshot = new VolumeSnapshot(
            [root, backup],
            ["/", "/System/Volumes/Data", "/Volumes/Backup", "/Volumes/Share"],
            "/",
            StringComparison.OrdinalIgnoreCase);

        var plan = ScanVolumePlanner.Plan(
            ["/Users/me/Documents", "/Volumes/Share/Team"],
            snapshot,
            ["/", "/Volumes/Backup"]);

        Assert.Equal(["/Users/me/Documents", "/Volumes/Backup"], plan.Roots);

        // The SMB share is a mount the catalog filtered out, so its folder is skipped — not scanned as part of "/".
        Assert.Equal("/Volumes/Share/Team", Assert.Single(plan.Skipped).Path);
    }

    [Fact]
    public void ListChoices_KeepsAMissingSelectedVolume_AsUnavailableAndStillSelected()
    {
        var choices = ScanVolumePlanner.ListChoices(WindowsSnapshot(C, D), [@"C:\", @"E:\"]);

        Assert.Equal([@"C:\", @"D:\", @"E:\"], choices.Select(c => c.RootPath));
        Assert.Equal([true, false, true], choices.Select(c => c.IsSelected));
        Assert.Equal([true, true, false], choices.Select(c => c.IsAvailable));
    }

    [Fact]
    public void ListChoices_DoesNotDuplicateAVolumeWhoseStoredRootDiffersOnlyInCaseOrSeparator()
    {
        var choices = ScanVolumePlanner.ListChoices(WindowsSnapshot(C), [@"c:"]);

        VolumeChoice only = Assert.Single(choices);
        Assert.True(only.IsSelected);
        Assert.True(only.IsAvailable);
    }

    [Fact]
    public void RemovableVolume_CanBeSelected()
    {
        var plan = ScanVolumePlanner.Plan(UserFolders, WindowsSnapshot(C, E), [@"C:\", @"E:\"]);

        Assert.Contains(@"E:\", plan.Roots);
    }
}
