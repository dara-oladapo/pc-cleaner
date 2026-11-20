using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PcCleaner.Core.DevCleanup;

/// <summary>Finds each WSL distro's virtual disk, for a read-only size report.</summary>
public interface IWslDiskLocator
{
    IReadOnlyList<VirtualDiskInfo> FindDisks();
}

/// <summary>
/// Reads <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss\{guid}</c>: <c>DistributionName</c>,
/// <c>BasePath</c> and <c>VhdFileName</c> (default <c>ext4.vhdx</c>). Never opens or modifies the disk.
/// </summary>
public sealed class RegistryWslDiskLocator : IWslDiskLocator
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    public IReadOnlyList<VirtualDiskInfo> FindDisks() => OperatingSystem.IsWindows() ? FindWindowsDisks() : [];

    [SupportedOSPlatform("windows")]
    private static List<VirtualDiskInfo> FindWindowsDisks()
    {
        var disks = new List<VirtualDiskInfo>();
        try
        {
            using RegistryKey? lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
            if (lxss is null)
            {
                return disks;
            }

            foreach (string id in lxss.GetSubKeyNames())
            {
                using RegistryKey? distro = lxss.OpenSubKey(id);
                if (distro?.GetValue("DistributionName") is not string name
                    || distro.GetValue("BasePath") is not string basePath)
                {
                    continue;
                }

                string fileName = distro.GetValue("VhdFileName") as string ?? "ext4.vhdx";
                string path = Path.Combine(StripLongPathPrefix(basePath), fileName);
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    disks.Add(new VirtualDiskInfo(name, path, info.Length));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A report, not a requirement: an unreadable registry just means no disk sizes.
        }

        return disks;
    }

    internal static string StripLongPathPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
}
