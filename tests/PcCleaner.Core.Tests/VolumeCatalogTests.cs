using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class VolumeCatalogTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class FakeVolumeSource(params MountedVolume[] volumes) : IVolumeSource
    {
        public IReadOnlyList<MountedVolume> GetMountedVolumes() => volumes;
    }

    private static MountedVolume Mount(
        string root,
        DriveType type = DriveType.Fixed,
        string label = "",
        string format = "NTFS",
        bool ready = true,
        VolumeTraits traits = VolumeTraits.None) =>
        new(root, label, type, format, ready, ready ? 500 * Gb : 0, ready ? 200 * Gb : 0, traits);

    private static VolumeSnapshot Windows(params MountedVolume[] mounts) =>
        new VolumeCatalog(new FakeVolumeSource(mounts), VolumePlatform.Windows, @"C:\").Read();

    private static VolumeSnapshot Mac(params MountedVolume[] mounts) =>
        new VolumeCatalog(new FakeVolumeSource(mounts), VolumePlatform.MacOS, "/").Read();

    [Fact]
    public void Windows_ListsFixedAndRemovable_AndOnlyTheSystemDriveIsOnByDefault()
    {
        var snapshot = Windows(
            Mount(@"C:\", label: "Windows"),
            Mount(@"D:\", label: "Data"),
            Mount(@"E:\", DriveType.Removable, format: "exFAT"));

        Assert.Equal([@"C:\", @"D:\", @"E:\"], snapshot.Volumes.Select(v => v.RootPath));
        Assert.Equal([ScanVolumeKind.System, ScanVolumeKind.Fixed, ScanVolumeKind.Removable], snapshot.Volumes.Select(v => v.Kind));
        Assert.Equal([@"C:\"], ScanVolumePlanner.DefaultSelection(snapshot));
    }

    [Fact]
    public void Windows_DisplayName_UsesLabelAndLetter_AndFallsBackToLocalDisk()
    {
        var snapshot = Windows(Mount(@"C:\", label: "Windows"), Mount(@"D:\"));

        Assert.Equal("Windows (C:)", snapshot.Volumes[0].DisplayName);
        Assert.Equal("Local Disk (D:)", snapshot.Volumes[1].DisplayName);
    }

    [Theory]
    [InlineData(DriveType.Network, "NTFS")]
    [InlineData(DriveType.CDRom, "CDFS")]
    [InlineData(DriveType.Ram, "NTFS")]
    [InlineData(DriveType.NoRootDirectory, "")]
    [InlineData(DriveType.Unknown, "NTFS")]
    public void Windows_NeverListsNetworkOpticalOrUnknownDrives(DriveType type, string format)
    {
        var snapshot = Windows(Mount(@"C:\"), Mount(@"Z:\", type, format: format));

        Assert.DoesNotContain(snapshot.Volumes, v => v.RootPath == @"Z:\");

        // Still a known mount, so a folder on it is attributed to it and skipped, not to C:.
        Assert.Contains(@"Z:\", snapshot.MountRoots);
    }

    [Fact]
    public void Windows_NotReadyDrive_IsNotListed()
    {
        var snapshot = Windows(Mount(@"C:\"), Mount(@"F:\", DriveType.Removable, ready: false));

        Assert.Single(snapshot.Volumes);
    }

    [Fact]
    public void Windows_VirtualDisk_IsListedButNotOnByDefault()
    {
        var snapshot = Windows(Mount(@"C:\"), Mount(@"V:\", label: "Dev Drive", format: "ReFS", traits: VolumeTraits.Virtual));

        ScanVolume vhd = Assert.Single(snapshot.Volumes, v => v.RootPath == @"V:\");
        Assert.Equal(ScanVolumeKind.Virtual, vhd.Kind);
        Assert.False(vhd.IsSelectedByDefault);
    }

    [Fact]
    public void Windows_SubstAliasAndCloudDrive_AreNotListed_EvenThoughDriveInfoCallsThemFixed()
    {
        // A real layout: K: is `subst` of a folder on C:, G: is Google Drive for desktop (FAT32, "Fixed").
        var snapshot = Windows(
            Mount(@"C:\", label: "OS"),
            Mount(@"D:\", label: "Data"),
            Mount(@"G:\", label: "Google Drive", format: "FAT32", traits: VolumeTraits.NoBackingDisk),
            Mount(@"K:\", label: "OS", traits: VolumeTraits.Alias));

        Assert.Equal([@"C:\", @"D:\"], snapshot.Volumes.Select(v => v.RootPath));
        Assert.Equal([@"C:\"], ScanVolumePlanner.DefaultSelection(snapshot));
    }

    [Fact]
    public void Windows_ReadOnlyDataDrive_IsNotListed()
    {
        var snapshot = Windows(Mount(@"C:\"), Mount(@"R:\", traits: VolumeTraits.ReadOnly));

        Assert.Single(snapshot.Volumes);
    }

    [Fact]
    public void Windows_SystemDrive_IsMatchedCaseInsensitively()
    {
        var snapshot = new VolumeCatalog(new FakeVolumeSource(Mount(@"c:\")), VolumePlatform.Windows, @"C:\").Read();

        Assert.True(Assert.Single(snapshot.Volumes).IsSystem);
    }

    [Fact]
    public void Mac_ListsRootAndVolumes_AndDropsSystemPlumbing()
    {
        var snapshot = Mac(
            Mount("/", label: "Macintosh HD", format: "apfs", traits: VolumeTraits.ReadOnly | VolumeTraits.Snapshot),
            Mount("/dev", DriveType.Ram, format: "devfs"),
            Mount("/System/Volumes/Data", format: "apfs"),
            Mount("/System/Volumes/VM", format: "apfs", traits: VolumeTraits.Hidden),
            Mount("/private/var/vm", format: "apfs"),
            Mount("/Library/Developer/CoreSimulator/Volumes/iOS_22A", format: "apfs"),
            Mount("/Volumes/Backup", format: "apfs"),
            Mount("/Volumes/USB", DriveType.Removable, format: "msdos"));

        Assert.Equal(["/", "/Volumes/Backup", "/Volumes/USB"], snapshot.Volumes.Select(v => v.RootPath));
        Assert.Equal("Macintosh HD", snapshot.Volumes[0].DisplayName);
        Assert.Equal(["/"], ScanVolumePlanner.DefaultSelection(snapshot));
    }

    [Fact]
    public void Mac_RootIsOfferedEvenThoughItIsASealedReadOnlySnapshot()
    {
        var snapshot = Mac(Mount("/", format: "apfs", traits: VolumeTraits.ReadOnly | VolumeTraits.Snapshot));

        ScanVolume root = Assert.Single(snapshot.Volumes);
        Assert.True(root.IsSystem);
        Assert.Equal("System volume", root.DisplayName);
    }

    [Theory]
    [InlineData("/Volumes/Installer", "hfs", VolumeTraits.ReadOnly, DriveType.Fixed)]
    [InlineData("/Volumes/Snap", "apfs", VolumeTraits.Snapshot, DriveType.Fixed)]
    [InlineData("/Volumes/Recovery", "apfs", VolumeTraits.Hidden, DriveType.Fixed)]
    [InlineData("/Volumes/iOS 18.2 Simulator", "apfs", VolumeTraits.None, DriveType.Fixed)]
    [InlineData("/Volumes/com.apple.TimeMachine.localsnapshots", "apfs", VolumeTraits.None, DriveType.Fixed)]
    [InlineData("/Volumes/com.apple.TimeMachine.localsnapshots/Backups.backupdb/x", "apfs", VolumeTraits.None, DriveType.Fixed)]
    [InlineData("/Volumes/Share", "smbfs", VolumeTraits.None, DriveType.Fixed)]
    [InlineData("/Volumes/Share2", "apfs", VolumeTraits.None, DriveType.Network)]
    [InlineData("/Volumes/Audio CD", "cddafs", VolumeTraits.None, DriveType.Fixed)]
    [InlineData("/Volumes/DVD", "udf", VolumeTraits.None, DriveType.CDRom)]
    public void Mac_ExcludesReadOnlySnapshotSimulatorNetworkAndOpticalMounts(string root, string format, VolumeTraits traits, DriveType type)
    {
        var snapshot = Mac(Mount("/", format: "apfs"), Mount(root, type, format: format, traits: traits));

        Assert.DoesNotContain(snapshot.Volumes, v => v.RootPath == root);
    }

    [Fact]
    public void Mac_VolumeName_FallsBackToTheMountFolderName()
    {
        var snapshot = Mac(Mount("/", format: "apfs"), Mount("/Volumes/Photos Archive", format: "apfs"));

        Assert.Equal("Photos Archive", snapshot.Volumes[1].DisplayName);
        Assert.Equal(ScanVolumeKind.External, snapshot.Volumes[1].Kind);
    }

    [Fact]
    public void SystemVolume_IsListedFirst_WhateverOrderTheOsReports()
    {
        var snapshot = Windows(Mount(@"D:\"), Mount(@"C:\"));

        Assert.Equal(@"C:\", snapshot.Volumes[0].RootPath);
    }

    [Fact]
    public void DefaultSelection_FallsBackToTheSystemRoot_WhenTheSystemVolumeCouldNotBeRead()
    {
        var snapshot = Windows(Mount(@"C:\", ready: false), Mount(@"D:\"));

        Assert.Equal([@"C:\"], ScanVolumePlanner.DefaultSelection(snapshot));
    }

    [Fact]
    public void UsedShare_IsZeroForAZeroCapacityVolume()
    {
        Assert.Equal(0, new ScanVolume(@"C:\", "C", ScanVolumeKind.System, 0, 0).UsedShare);
        Assert.Equal(0.6, new ScanVolume(@"C:\", "C", ScanVolumeKind.System, 1000, 400).UsedShare, precision: 10);
    }
}
