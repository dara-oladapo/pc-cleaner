using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class CloudProviderClassifierTests
{
    [Theory]
    [InlineData("OneDrive!S-1-5-21-1!Personal|e66c", "OneDrive - Personal", CloudProvider.OneDrive)]
    [InlineData("OneDrive!S-1-5-21-1!Business1|abc", "OneDrive - Contoso", CloudProvider.OneDrive)]
    [InlineData("Dropbox!S-1-5-21-1!12345", "Dropbox", CloudProvider.Dropbox)]
    [InlineData("GoogleDriveFS!S-1-5-21-1!x", "Google Drive", CloudProvider.GoogleDrive)]
    [InlineData("iCloudDrive!S-1-5-21-1!x", "iCloud Drive", CloudProvider.ICloudDrive)]
    [InlineData("Box!S-1-5-21-1!x", "Box", CloudProvider.Box)]
    [InlineData("SynologyDrive!S-1-5-21-1!session.1", "Synology Drive (nas, drive)", CloudProvider.SynologyDrive)]
    [InlineData("Contoso!S-1-5-21-1!x", "@C:\\x.dll,-1", CloudProvider.Other)]
    [InlineData("Contoso!S-1-5-21-1!x", "Contoso Dropbox Mirror", CloudProvider.Dropbox)]
    [InlineData("Sandbox!S-1-5-21-1!x", "Sandbox", CloudProvider.Other)]
    public void Classify_RecognisesKnownProviders(string id, string display, CloudProvider expected) =>
        Assert.Equal(expected, CloudProviderClassifier.Classify(id, display));

    [Theory]
    [InlineData("CrossDevice!!S-1-5-21-1!CrossDeviceRoot130e", false)]
    [InlineData("OneDrive!S-1-5-21-1!Personal", true)]
    [InlineData("SynologyDrive!S-1-5-21-1!x", true)]
    public void IsCloudStorage_ExcludesPhoneLinkRoots(string id, bool expected) =>
        Assert.Equal(expected, CloudProviderClassifier.IsCloudStorage(id));
}
