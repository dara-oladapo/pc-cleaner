using System.Diagnostics;

namespace PcCleaner.Core.Tests;

/// <summary>Creates directory links inside a test's own temp directory.</summary>
internal static class TestLinks
{
    /// <summary>
    /// A junction on Windows (no admin or developer mode needed, unlike a symlink) and a symlink elsewhere.
    /// Returns false if the platform refused, so the calling test can bail out instead of failing spuriously.
    /// </summary>
    public static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? process = Process.Start(psi);
            process?.WaitForExit();
            return Directory.Exists(linkPath)
                && new DirectoryInfo(linkPath).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        Directory.CreateSymbolicLink(linkPath, targetPath);
        return true;
    }
}
