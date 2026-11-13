using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Win32 and Cloud Files API (cfapi) calls used to read placeholder state and to mark files online-only.
/// </summary>
/// <remarks>
/// Structures are read from byte buffers at documented offsets rather than marshalled as C# structs, so a
/// layout mistake shows up as a wrong number in one place rather than as memory corruption.
/// Nothing here opens a file for reading its content, so nothing here downloads a dehydrated file.
/// </remarks>
internal static class CloudFilesNative
{
    public const uint FileAttributeSystem = 0x4;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;
    public const uint FileAttributeOffline = 0x1000;
    public const uint FileAttributePinned = 0x80000;
    public const uint FileAttributeUnpinned = 0x100000;
    public const uint FileAttributeRecallOnDataAccess = 0x400000;

    private const uint FileReadData = 0x1;
    private const uint FileReadAttributes = 0x80;
    private const uint Synchronize = 0x100000;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint FileShareNone = 0;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private const int FileBasicInfoClass = 0;
    private const int FileStandardInfoClass = 1;
    private const int FileAttributeTagInfoClass = 9;

    private const int CfPlaceholderInfoStandard = 1;
    private const int CfPinStateUnpinned = 2;
    private const int CfSetPinFlagNone = 0;

    /// <summary>HRESULT_FROM_WIN32(ERROR_NOT_A_CLOUD_FILE).</summary>
    private const int HResultNotACloudFile = unchecked((int)0x80070178);

    /// <summary>HRESULT_FROM_WIN32(ERROR_MORE_DATA). The fixed part of the structure is still filled in.</summary>
    private const int HResultMoreData = unchecked((int)0x800700EA);

    /// <summary>PHCM_EXPOSE_PLACEHOLDERS: show this thread placeholders as what they are, not as plain files.</summary>
    private const sbyte PhcmExposePlaceholders = 2;

    /// <summary>
    /// All IO_REPARSE_TAG_CLOUD_* tags (0x9000001A, 0x9000101A … 0x9000F01A) differ only in bits 12–15.
    /// </summary>
    public static bool IsCloudReparseTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;

    /// <summary>
    /// Windows hides placeholders from processes that have not said they understand them. This exposes them
    /// to the calling thread only, so the app's other scanners keep their existing behaviour.
    /// </summary>
    public static PlaceholderModeScope ExposePlaceholdersOnThisThread() => new(RtlSetThreadPlaceholderCompatibilityMode(PhcmExposePlaceholders));

    internal readonly struct PlaceholderModeScope(sbyte previous) : IDisposable
    {
        public void Dispose() => RtlSetThreadPlaceholderCompatibilityMode(previous);
    }

    /// <summary>
    /// Opens a handle that can read attributes and placeholder state only. Never follows a link, and never
    /// downloads content.
    /// </summary>
    public static SafeFileHandle OpenForMetadata(string path) =>
        CreateFileW(path, FileReadAttributes | Synchronize, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);

    /// <summary>
    /// Opens the file with no sharing, which fails if any other program has it open. The handle has read-data
    /// access, which <c>CfSetPinState</c> requires, but no data is ever read through it.
    /// </summary>
    public static SafeFileHandle OpenExclusive(string path, out bool inUse)
    {
        SafeFileHandle handle = CreateFileW(path, FileReadData | FileReadAttributes | Synchronize, FileShareNone, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
        int error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        inUse = error is ErrorSharingViolation or ErrorLockViolation;
        return handle;
    }

    /// <summary>True when another program has the file open. Opens and closes it at once.</summary>
    public static bool IsOpenElsewhere(string path)
    {
        using SafeFileHandle handle = OpenExclusive(path, out bool inUse);
        return inUse;
    }

    public static long? GetAllocationSize(SafeFileHandle handle)
    {
        // FILE_STANDARD_INFO: LARGE_INTEGER AllocationSize; LARGE_INTEGER EndOfFile; DWORD NumberOfLinks; BOOLEAN DeletePending; BOOLEAN Directory.
        byte[] buffer = new byte[24];
        return GetFileInformationByHandleEx(handle, FileStandardInfoClass, buffer, (uint)buffer.Length)
            ? BinaryPrimitives.ReadInt64LittleEndian(buffer)
            : null;
    }

    public static (DateTime LastAccessUtc, DateTime LastWriteUtc, uint Attributes)? GetBasicInfo(SafeFileHandle handle)
    {
        // FILE_BASIC_INFO: LARGE_INTEGER CreationTime, LastAccessTime, LastWriteTime, ChangeTime; DWORD FileAttributes.
        byte[] buffer = new byte[40];
        if (!GetFileInformationByHandleEx(handle, FileBasicInfoClass, buffer, (uint)buffer.Length))
        {
            return null;
        }

        return (
            FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(8))),
            FromFileTime(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(16))),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(32)));
    }

    public static uint? GetReparseTag(SafeFileHandle handle)
    {
        // FILE_ATTRIBUTE_TAG_INFO: DWORD FileAttributes; DWORD ReparseTag.
        byte[] buffer = new byte[8];
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, buffer, (uint)buffer.Length))
        {
            return null;
        }

        uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        return (attributes & FileAttributeReparsePoint) != 0 ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)) : 0;
    }

    /// <summary>Placeholder state from <c>CfGetPlaceholderInfo</c>, or null when the file is not a placeholder.</summary>
    public static PlaceholderInfo? GetPlaceholderInfo(SafeFileHandle handle)
    {
        // CF_PLACEHOLDER_STANDARD_INFO: OnDiskDataSize@0, ValidatedDataSize@8, ModifiedDataSize@16, PropertiesSize@24,
        // PinState@32, InSyncState@36, FileId@40, SyncRootFileId@48, FileIdentityLength@56, FileIdentity@60.
        // The identity is at most 4 KB (CF_PLACEHOLDER_MAX_FILE_IDENTITY_LENGTH).
        byte[] buffer = new byte[64 + 4096];
        int hr = CfGetPlaceholderInfo(handle, CfPlaceholderInfoStandard, buffer, (uint)buffer.Length, out _);

        if (hr == HResultNotACloudFile)
        {
            return null;
        }

        if (hr < 0 && hr != HResultMoreData)
        {
            // A placeholder whose state could not be read: report it with an unknown sync state, which the
            // eligibility rules treat as "not in sync".
            return new PlaceholderInfo(CloudPinState.Unspecified, CloudSyncState.Unknown, 0);
        }

        long modified = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(16));
        int pin = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32));
        int inSync = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(36));

        CloudPinState pinState = pin switch
        {
            0 => CloudPinState.Unspecified,
            1 => CloudPinState.Pinned,
            2 => CloudPinState.Unpinned,
            3 => CloudPinState.Excluded,
            4 => CloudPinState.Inherit,

            // An unknown value from a future Windows: assume the user wants it kept.
            _ => CloudPinState.Pinned,
        };

        CloudSyncState syncState = inSync switch
        {
            1 => CloudSyncState.InSync,
            0 => CloudSyncState.NotInSync,
            _ => CloudSyncState.Unknown,
        };

        return new PlaceholderInfo(pinState, syncState, modified);
    }

    /// <summary>
    /// Marks the file online-only: <c>CfSetPinState(CF_PIN_STATE_UNPINNED)</c>, the same as <c>attrib +U -P</c>
    /// and Explorer's "Free up space". The provider then releases the local copy itself. Returns the HRESULT.
    /// </summary>
    public static int SetUnpinned(SafeFileHandle handle) => CfSetPinState(handle, CfPinStateUnpinned, CfSetPinFlagNone, IntPtr.Zero);

    internal sealed record PlaceholderInfo(CloudPinState PinState, CloudSyncState SyncState, long ModifiedBytes);

    // --- Directory enumeration ------------------------------------------------------------------------------

    public const int FindExInfoBasic = 1;
    public const int FindExSearchNameMatch = 0;
    public const int FindFirstExLargeFetch = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly DateTime ToUtc() => FromFileTime(((long)High << 32) | Low);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Win32FindData
    {
        public uint Attributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;

        /// <summary>The reparse tag, when <see cref="Attributes"/> has FILE_ATTRIBUTE_REPARSE_POINT.</summary>
        public uint Reserved0;

        public uint Reserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;

        public readonly long Length => ((long)FileSizeHigh << 32) | FileSizeLow;
    }

    public sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeFindHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => FindClose(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFindHandle FindFirstFileExW(
        string fileName, int infoLevel, out Win32FindData findData, int searchOp, IntPtr searchFilter, int additionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextFileW(SafeFindHandle findFile, out Win32FindData findData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);

    // --- Handles and file information -----------------------------------------------------------------------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, [Out] byte[] info, uint bufferSize);

    // --- Cloud Files API ------------------------------------------------------------------------------------

    [DllImport("cldapi.dll")]
    private static extern int CfGetPlaceholderInfo(SafeFileHandle fileHandle, int infoClass, [Out] byte[] infoBuffer, uint infoBufferLength, out uint returnedLength);

    [DllImport("cldapi.dll")]
    private static extern int CfSetPinState(SafeFileHandle fileHandle, int pinState, int pinFlags, IntPtr overlapped);

    [DllImport("ntdll.dll")]
    private static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

    // An out-of-range time reads as "now-ish" to the age rule only if it is in the future, so map nonsense to
    // MaxValue: a file with an unreadable time is never old enough to release.
    private static DateTime FromFileTime(long fileTime) =>
        fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc() ? DateTime.MaxValue : DateTime.FromFileTimeUtc(fileTime);
}
