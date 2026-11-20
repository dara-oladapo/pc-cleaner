namespace PcCleaner.Core.Tests;

/// <summary>Creates an isolated temp directory for a single test and deletes it on dispose.</summary>
public sealed class TestDirectory : IDisposable
{
    public string Path { get; }

    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PcCleanerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string content = "x")
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string WriteFileOfSize(string relativePath, int sizeBytes)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                RemoveLinks(Path);
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Unlink any junction/symlink a test created (non-recursive delete removes the link, never its target) so the
    // recursive delete below only ever sees real directories.
    private static void RemoveLinks(string dir)
    {
        foreach (string sub in Directory.GetDirectories(dir))
        {
            if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(sub);
            }
            else
            {
                RemoveLinks(sub);
            }
        }
    }
}
