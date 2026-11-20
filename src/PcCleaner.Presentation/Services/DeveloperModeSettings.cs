namespace PcCleaner.Presentation.Services;

/// <summary>
/// The persisted "Developer mode" setting, over <see cref="ISettingsStore"/>. Off by default and never turned on
/// without the user saying yes. While off: the Developer page is hidden, no Docker, WSL or git command runs, and
/// junk sources marked <c>RequiresDeveloperMode</c> are left out of the junk scan.
/// </summary>
public sealed class DeveloperModeSettings(ISettingsStore store)
{
    private const string EnabledKey = "developer_mode";
    private const string SuggestionShownKey = "developer_mode_suggestion_shown";

    /// <summary>Raised after <see cref="IsEnabled"/> changes.</summary>
    public event EventHandler? Changed;

    public bool IsEnabled
    {
        get => store.Get(EnabledKey) == bool.TrueString;
        set
        {
            if (IsEnabled == value)
            {
                return;
            }

            store.Set(EnabledKey, value ? bool.TrueString : bool.FalseString);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Set once the one-time "turn on developer mode?" suggestion has been shown, whatever the answer.</summary>
    public bool SuggestionShown
    {
        get => store.Get(SuggestionShownKey) == bool.TrueString;
        set => store.Set(SuggestionShownKey, value ? bool.TrueString : bool.FalseString);
    }
}
