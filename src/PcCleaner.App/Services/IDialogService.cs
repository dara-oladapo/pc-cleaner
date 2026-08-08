namespace PcCleaner.App.Services;

/// <summary>
/// Asks the user to confirm something before it happens.
/// </summary>
/// <remarks>
/// This is an interface rather than a direct <c>DisplayAlert</c> call so the view models that guard a
/// destructive action stay unit-testable — a test can assert "declining the dialog deletes nothing"
/// without a UI. Before this existed, "Clean Selected" went straight to a permanent delete with no
/// confirmation at all.
/// </remarks>
public interface IDialogService
{
    /// <summary>Returns true when the user chooses the affirmative action.</summary>
    Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText = "Cancel");
}
