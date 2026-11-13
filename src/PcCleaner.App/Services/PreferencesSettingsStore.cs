using PcCleaner.Presentation.Services;

namespace PcCleaner.App.Services;

/// <summary>
/// <see cref="ISettingsStore"/> over MAUI <c>Preferences</c>: the registry on Windows, NSUserDefaults on
/// macOS. Survives restarts, updates and a reinstall-over, the same as the theme choice.
/// </summary>
public sealed class PreferencesSettingsStore : ISettingsStore
{
    public string? Get(string key) =>
        Preferences.Default.ContainsKey(key) ? Preferences.Default.Get<string?>(key, null) : null;

    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
