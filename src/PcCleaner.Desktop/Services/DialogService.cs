using Avalonia.Threading;
using PcCleaner.Presentation.Services;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop.Services;

/// <summary>Drives the app's own confirmation overlay in <c>MainWindow.axaml</c>, as the MAUI app's DialogHost does.</summary>
/// <remarks>
/// Not an OS message box, for the same reasons as on MAUI: it couldn't show the manifest of what is about to
/// be deleted, and the most consequential moment in the app would look like somebody else's UI.
/// </remarks>
public sealed class DialogService(DialogHostViewModel host) : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel") =>
        ConfirmAsync(title, message, acceptText, cancelText, isDestructive: true, details: null);

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string acceptText,
        string cancelText,
        bool isDestructive,
        IReadOnlyList<string>? details) =>
        Dispatcher.UIThread.InvokeAsync(
            () => host.ShowAsync(title, message, acceptText, cancelText, isDestructive, details));
}
