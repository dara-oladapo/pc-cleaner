using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Asks Windows what <see cref="DriveInfo"/> can't say about a drive letter: is it a real disk partition
/// or a <c>subst</c> alias or cloud-drive driver, is it a file-backed virtual disk (an attached VHD/VHDX,
/// a Dev Drive), and is it mounted read-only.
/// </summary>
/// <remarks>
/// Read-only queries only. The volume is opened with zero access rights, which Windows allows without
/// elevation for <c>IOCTL_STORAGE_QUERY_PROPERTY</c>; nothing on the volume is read or written.
/// Any failure returns "nothing known", which leaves the drive classified by its <see cref="DriveType"/>.
/// </remarks>
public sealed class WindowsVolumeTraitsProbe : IVolumeTraitsProbe
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FileReadOnlyVolume = 0x00080000;

    // STORAGE_BUS_TYPE. FileBackedVirtual is what an attached VHD, VHDX or Dev Drive reports.
    private const int BusTypeFileBackedVirtual = 0xF;

    public VolumeProbeResult Probe(string rootPath)
    {
        if (rootPath.Length < 2 || rootPath[1] != ':')
        {
            return VolumeProbeResult.Unknown;
        }

        string letter = rootPath[..2];
        VolumeTraits traits = VolumeTraits.None;

        // What the letter points at. A disk partition (physical, USB, or an attached VHD) is
        // \Device\HarddiskVolumeN. A subst drive is "\??\C:\some\folder". Anything else is a file-system
        // driver with no disk behind it: Google Drive for desktop shows up as \Device\Volume{guid}.
        string? target = QueryDosDeviceTarget(letter);
        if (target is not null)
        {
            if (target.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                return new VolumeProbeResult(VolumeTraits.Alias);
            }

            if (!target.StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
            {
                return new VolumeProbeResult(VolumeTraits.NoBackingDisk);
            }
        }

        if (IsFileBackedVirtual(letter))
        {
            traits |= VolumeTraits.Virtual;
        }

        if (IsReadOnlyVolume(rootPath))
        {
            traits |= VolumeTraits.ReadOnly;
        }

        return new VolumeProbeResult(traits);
    }

    private static bool IsFileBackedVirtual(string driveLetter)
    {
        using SafeFileHandle handle = CreateFileW(
            $@"\\.\{driveLetter}", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        // StorageDeviceProperty (0), PropertyStandardQuery (0).
        var query = new StoragePropertyQuery();
        byte[] buffer = new byte[1024];

        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                buffer, buffer.Length, out int returned, IntPtr.Zero) || returned < 32)
        {
            return false;
        }

        // STORAGE_DEVICE_DESCRIPTOR: VendorIdOffset @12, ProductIdOffset @16, BusType @28.
        if (BitConverter.ToInt32(buffer, 28) == BusTypeFileBackedVirtual)
        {
            return true;
        }

        // Older Windows reports an attached VHD as vendor "Msft", product "Virtual Disk" on another bus.
        string vendor = ReadAnsi(buffer, BitConverter.ToInt32(buffer, 12), returned);
        string product = ReadAnsi(buffer, BitConverter.ToInt32(buffer, 16), returned);
        return vendor.Equals("Msft", StringComparison.OrdinalIgnoreCase)
            && product.StartsWith("Virtual Disk", StringComparison.OrdinalIgnoreCase);
    }

    private static string? QueryDosDeviceTarget(string driveLetter)
    {
        var buffer = new char[1024];
        uint length = QueryDosDeviceW(driveLetter, buffer, buffer.Length);
        if (length == 0)
        {
            return null;
        }

        // The result is a list of NUL-terminated strings; the first is the current mapping.
        int end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? (int)length : end);
    }

    private static bool IsReadOnlyVolume(string rootPath)
    {
        bool ok = GetVolumeInformationW(rootPath, null, 0, out _, out _, out uint flags, null, 0);
        return ok && (flags & FileReadOnlyVolume) != 0;
    }

    private static string ReadAnsi(byte[] buffer, int offset, int length)
    {
        if (offset <= 0 || offset >= length)
        {
            return string.Empty;
        }

        int end = Array.IndexOf(buffer, (byte)0, offset, length - offset);
        return Encoding.ASCII.GetString(buffer, offset, (end < 0 ? length : end) - offset).Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public int AdditionalParameters;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, int maxLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, ref StoragePropertyQuery inBuffer, int inBufferSize,
        [Out] byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName, StringBuilder? volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber,
        out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemNameBuffer, int fileSystemNameSize);
}
