namespace PcCleaner.Core.Models;

public enum JunkCategory
{
    TempFiles,
    BrowserCache,
    SystemCache,
    SystemLogs,
    PackageManagerCache,
    EmptyFolder,
    TrashBin,
    DeveloperCache,

    /// <summary>A project's git-ignored build output or dependency folder (bin, obj, node_modules, target...).</summary>
    BuildArtifacts,
}
