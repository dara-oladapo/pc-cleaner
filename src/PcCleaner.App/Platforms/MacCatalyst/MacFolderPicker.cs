using PcCleaner.App.Services;
using UIKit;
using UniformTypeIdentifiers;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>Opens the macOS folder browser via the document picker restricted to folders.</summary>
/// <remarks>
/// Untested on real hardware, like the rest of the Mac Catalyst target (see README issue #4). Every failure
/// path returns null so the typed-path entry remains a working fallback.
/// </remarks>
public sealed class MacFolderPicker : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var presenter = TopViewController();
                if (presenter is null)
                {
                    completion.TrySetResult(null);
                    return;
                }

                // asCopy: false — we want the real path to scan, not a sandbox copy of it. The app ships
                // unsandboxed on macOS precisely so it can read arbitrary user folders.
                var picker = new UIDocumentPickerViewController([UTTypes.Folder], asCopy: false);

                picker.DidPickDocumentAtUrls += (_, e) =>
                    completion.TrySetResult(e.Urls.Length > 0 ? e.Urls[0].Path : null);

                picker.WasCancelled += (_, _) => completion.TrySetResult(null);

                presenter.PresentViewController(picker, animated: true, completionHandler: null);
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
        });

        return completion.Task.WaitAsync(cancellationToken);
    }

    private static UIViewController? TopViewController()
    {
        var root = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)?
            .RootViewController;

        while (root?.PresentedViewController is not null)
        {
            root = root.PresentedViewController;
        }

        return root;
    }
}
