namespace PcCleaner.App.Services;

/// <summary>
/// Asks the user to confirm something before it happens, using the app's own dialog rather than the
/// operating system's.
/// </summary>
/// <remarks>
/// This is an interface rather than a direct call into the UI so the view models that guard a destructive
/// action stay unit-testable — a test can assert "declining the dialog deletes nothing" without a UI.
/// Before this existed, "Clean Selected" went straight to a permanent delete with no confirmation at all.
/// </remarks>
public interface IDialogService
{
    /// <summary>Returns true when the user chooses the affirmative action.</summary>
    Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel");

    /// <summary>
    /// As above, plus a short manifest of what is about to happen — the largest few items by size, and a
    /// "+ N more" line. Naming what will actually go is the difference between a confirmation the user can
    /// act on and one they dismiss reflexively.
    /// </summary>
    /// <param name="isDestructive">Paints the affirmative button with Danger. Reserve it for removing data.</param>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string acceptText,
        string cancelText,
        bool isDestructive,
        IReadOnlyList<string>? details);
}
