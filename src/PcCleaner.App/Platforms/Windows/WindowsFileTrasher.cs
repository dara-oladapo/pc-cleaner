using System.Runtime.InteropServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>Moves files/folders to the Recycle Bin via the classic shell32 SHFileOperation API (reversible, unlike File.Delete).</summary>
public sealed class WindowsFileTrasher : IFileTrasher
{
    public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = GetSize(path);
            int result = SendToRecycleBin(path);
            if (result == 0)
            {
                succeeded.Add(path);
                bytesFreed += size;
            }
            else
            {
                failed.Add((path, $"Shell operation failed with code {result}"));
            }
        }

        return Task.FromResult(new TrashResult(succeeded, failed, bytesFreed));
    }

    private static long GetSize(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => TryGetLength(f));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

    private static long TryGetLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int SendToRecycleBin(string path)
    {
        var shf = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // pFrom must be double-null-terminated.
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        return SHFileOperation(ref shf);
    }

    private const int FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);
}
