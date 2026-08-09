using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Services;

/// <summary>
/// Drives the app's own confirmation dialog (see <c>Controls/DialogHost.xaml</c>).
/// </summary>
/// <remarks>
/// Deliberately not <c>Page.DisplayAlert</c>. That renders the operating system's dialog: it ignores the
/// design system, differs between Windows and Mac Catalyst, can't list what is about to be deleted, and
/// reads as a system warning rather than part of this app. It's also obsolete in .NET 10.
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
        MainThread.InvokeOnMainThreadAsync(
            () => host.ShowAsync(title, message, acceptText, cancelText, isDestructive, details));
}
