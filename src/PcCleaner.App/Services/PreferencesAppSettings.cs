using PcCleaner.Core.Abstractions;

namespace PcCleaner.App.Services;

/// <summary>
/// <see cref="IAppSettings"/> over MAUI <c>Preferences</c> (registry-backed on Windows, NSUserDefaults on macOS),
/// so settings survive restarts and updates. Developer mode defaults to off.
/// </summary>
public sealed class PreferencesAppSettings : IAppSettings
{
    private const string DeveloperModeKey = "developer_mode";
    private const string SuggestionShownKey = "developer_mode_suggestion_shown";

    public event EventHandler? Changed;

    public bool DeveloperMode
    {
        get => Preferences.Default.Get(DeveloperModeKey, false);
        set => Set(DeveloperModeKey, value);
    }

    public bool DeveloperModeSuggestionShown
    {
        get => Preferences.Default.Get(SuggestionShownKey, false);
        set => Set(SuggestionShownKey, value);
    }

    private void Set(string key, bool value)
    {
        if (Preferences.Default.Get(key, false) == value)
        {
            return;
        }

        Preferences.Default.Set(key, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
