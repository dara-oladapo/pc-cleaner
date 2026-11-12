using System.Diagnostics;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

/// <summary>
/// A junction (Windows) or symlink (macOS/Linux) inside a junk root must never lead the cleaner outside that root —
/// the root may be cleaned by the elevated helper, where following a link planted by a standard user would let them
/// delete arbitrary directories as admin.
/// </summary>
public class JunkCleanerReparsePointTests
{
    [Fact]
    public async Task CleanAsync_DoesNotFollowDirectoryLinksOutOfTheRoot()
    {
        using var dir = new TestDirectory();
        string root = Path.Combine(dir.Path, "root");
        string outside = Path.Combine(dir.Path, "outside");
        string emptyOutside = Path.Combine(outside, "empty");
        string fileOutside = dir.WriteFileOfSize("outside/keep.bin", 10);
        Directory.CreateDirectory(emptyOutside);
        dir.WriteFileOfSize("root/junk.bin", 10);

        string link = Path.Combine(root, "link");
        CreateDirectoryLink(link, outside);

        try
        {
            var result = await new JunkCleaner().CleanAsync(
                [new JunkItem(root, SizeBytes: 10, JunkCategory.SystemCache, "Cache", IsDirectory: true)]);

            Assert.True(result.AllSucceeded);
            Assert.True(Directory.Exists(emptyOutside), "empty directory behind the link was deleted");
            Assert.True(File.Exists(fileOutside), "file behind the link was deleted");
        }
        finally
        {
            // Remove the link itself (never its target) first; a recursive delete of the test folder can trip on it.
            if (Directory.Exists(link) || File.Exists(link))
            {
                Directory.Delete(link);
            }
        }
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            // A junction, not a symlink: creating one needs no special privilege, which is exactly why a standard user
            // could plant it.
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkPath);
            startInfo.ArgumentList.Add(targetPath);

            using Process process = Process.Start(startInfo)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }

        Assert.True(new DirectoryInfo(linkPath).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}
