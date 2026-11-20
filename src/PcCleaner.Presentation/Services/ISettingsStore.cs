namespace PcCleaner.Presentation.Services;

/// <summary>
/// Persistent string key/value settings. Deliberately tiny and generic: each feature owns its own key and
/// its own serialisation, so adding a setting never means growing this interface.
/// </summary>
/// <remarks>The MAUI app backs this with <c>Preferences</c> (registry on Windows, NSUserDefaults on macOS).</remarks>
public interface ISettingsStore
{
    /// <summary>The stored value, or null when the key has never been written.</summary>
    string? Get(string key);

    void Set(string key, string value);
}
