using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PcCleaner.Desktop.Services;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Opens the desktop's folder browser through Avalonia's <see cref="IStorageProvider"/>.</summary>
/// <remarks>
/// <para>
/// On Linux Avalonia routes this through <c>xdg-desktop-portal</c> when one is running (so it works under Flatpak and
/// shows the desktop's own dialog), and falls back to a GTK dialog when it isn't. Nothing here is Linux-specific;
/// it lives under Platforms/Linux only because that is the one platform this front end serves today.
/// </para>
/// <para>
/// Same contract as the MAUI pickers: null on cancel <em>or</em> on any failure, never an exception. The typed-path
/// field beside Browse stays on screen, so a picker that won't open is an inconvenience rather than a dead end.
/// </para>
/// </remarks>
public sealed class LinuxFolderPicker(ITopLevelAccessor topLevelAccessor) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The storage provider belongs to the UI thread. View models call this from a command, which is
            // already there; the hop is for any caller that isn't.
            return Dispatcher.UIThread.CheckAccess()
                ? await PickOnUiThreadAsync(cancellationToken)
                : await Dispatcher.UIThread.InvokeAsync(() => PickOnUiThreadAsync(cancellationToken));
        }
        catch (Exception)
        {
            // Includes OperationCanceledException: cancelling the token is a cancel, and a cancel is null.
            return null;
        }
    }

    private async Task<string?> PickOnUiThreadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (topLevelAccessor.Current?.StorageProvider is not { CanPickFolder: true } storageProvider)
        {
            return null;
        }

        var folders = await storageProvider
            .OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder", AllowMultiple = false })
            .WaitAsync(cancellationToken);

        try
        {
            // A portal can hand back a non-file URI (a network location, say). TryGetLocalPath is null for those,
            // which is the right answer: the scanners can only walk local paths.
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        finally
        {
            foreach (var folder in folders)
            {
                folder.Dispose();
            }
        }
    }
}
