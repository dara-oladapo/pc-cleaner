namespace PcCleaner.App.Services;

/// <summary>Opens the OS folder browser so scan roots can be chosen instead of typed.</summary>
/// <remarks>
/// CommunityToolkit.Maui ships a FolderPicker but can't be referenced here — its current version forces a
/// Microsoft.Maui.Controls downgrade against the workload this project builds on (see README). This is the
/// same platform-service pattern the junk scanners and trashers already use.
/// </remarks>
public interface IFolderPickerService
{
    /// <summary>Returns the chosen folder path, or null if the user cancelled or the picker is unavailable.</summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
