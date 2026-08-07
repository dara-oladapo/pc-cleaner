using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Scanning;

/// <summary>Groups files by size first (cheap), then hashes only same-size candidates (SHA-256) to confirm duplicates.</summary>
public sealed class DuplicateFileScanner : IDuplicateFileScanner
{
    public async IAsyncEnumerable<DuplicateGroup> ScanAsync(
        IReadOnlyList<string> rootPaths,
        long minFileSizeBytes = 1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        var bySize = new Dictionary<long, List<FileEntry>>();

        // FileSystemWalker's own counter resets per root, so accumulate an offset to keep the
        // reported count running across every folder the user added.
        int scannedBeforeCurrentRoot = 0;
        foreach (string root in rootPaths)
        {
            int offset = scannedBeforeCurrentRoot;
            IProgress<int>? rootProgress = filesScanned is null
                ? null
                : new Progress<int>(count => filesScanned.Report(offset + count));

            int lastCountInRoot = 0;
            await foreach (FileEntry entry in FileSystemWalker.EnumerateFilesAsync(root, cancellationToken, rootProgress))
            {
                lastCountInRoot++;
                if (entry.SizeBytes < minFileSizeBytes)
                {
                    continue;
                }

                if (!bySize.TryGetValue(entry.SizeBytes, out List<FileEntry>? list))
                {
                    list = [];
                    bySize[entry.SizeBytes] = list;
                }

                list.Add(entry);
            }

            scannedBeforeCurrentRoot += lastCountInRoot;
        }

        foreach ((long size, List<FileEntry> candidates) in bySize)
        {
            if (candidates.Count < 2)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var byHash = new Dictionary<string, List<FileEntry>>();
            foreach (FileEntry candidate in candidates)
            {
                string? hash = await TryComputeHashAsync(candidate.Path, cancellationToken);
                if (hash is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out List<FileEntry>? list))
                {
                    list = [];
                    byHash[hash] = list;
                }

                list.Add(candidate);
            }

            foreach ((string hash, List<FileEntry> files) in byHash)
            {
                if (files.Count > 1)
                {
                    yield return new DuplicateGroup(hash, size, files);
                }
            }
        }
    }

    private static async Task<string?> TryComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexStringLower(hash);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
