namespace PcCleaner.App.Services;

/// <summary>
/// <see cref="IDialogService"/> over MAUI's built-in alert. No third-party dependency — CommunityToolkit.Maui
/// can't be referenced here (see README: it forces a Microsoft.Maui.Controls downgrade against this workload).
/// </summary>
public sealed class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel")
    {
        var page = CurrentPage();
        if (page is null)
        {
            // No page means no way to ask. Refusing is the safe answer: every caller is guarding a
            // delete, so "couldn't ask" must never be read as "user said yes".
            return false;
        }

        return await MainThread.InvokeOnMainThreadAsync(() => page.DisplayAlert(title, message, acceptText, cancelText));
    }

    private static Page? CurrentPage() =>
        Shell.Current?.CurrentPage
        ?? Application.Current?.Windows.FirstOrDefault()?.Page;
}
