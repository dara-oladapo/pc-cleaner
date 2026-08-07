namespace PcCleaner.Core.Models;

public sealed record FileEntry(string Path, long SizeBytes, DateTime LastModifiedUtc);
