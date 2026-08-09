namespace PcCleaner.App.Services;

/// <summary>What the user chose in the theme switcher, which is not the same as which theme is showing.</summary>
public enum ThemePreference
{
    /// <summary>Follow the operating system, and keep following it when the OS changes. The default.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Reads, applies, and remembers the user's theme choice.</summary>
public interface IThemeService
{
    ThemePreference Current { get; }

    /// <summary>Applies the preference to the running app and persists it for every future launch.</summary>
    void Set(ThemePreference preference);

    /// <summary>Re-applies the stored preference. Call once during startup, before the first window is shown.</summary>
    void ApplyStored();
}
