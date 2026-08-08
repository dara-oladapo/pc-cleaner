using PcCleaner.App.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>Opens the Windows folder browser via the WinRT picker.</summary>
/// <remarks>
/// An unpackaged app (this project sets WindowsPackageType=None) has no implicit window identity, so the
/// picker must be handed the app's HWND before it is shown or it throws. That is what
/// <see cref="InitializeWithWindow"/> does here.
/// </remarks>
public sealed class WindowsFolderPicker : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            {
                return null;
            }

            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };

            // The picker refuses to open with an empty filter list, even though folders are what it returns.
            picker.FileTypeFilter.Add("*");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);
            return folder?.Path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            // The typed-path entry is still on screen as a fallback, so a picker that won't open is an
            // inconvenience rather than a dead end.
            return null;
        }
    }
}
