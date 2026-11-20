using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Stub until #13. Always "unavailable", which the contract already allows: null, never an exception.</summary>
/// <remarks>The typed-path field beside Browse stays usable, so a missing picker is an inconvenience, not a dead end.</remarks>
public sealed class LinuxFolderPicker : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
