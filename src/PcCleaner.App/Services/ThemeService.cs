namespace PcCleaner.App.Services;

/// <summary>
/// <see cref="IThemeService"/> over MAUI's <c>UserAppTheme</c> and <c>Preferences</c>.
/// </summary>
/// <remarks>
/// Before this the app read the OS theme and offered no way to override it — fine until you want a dark
/// app on a light desktop. The choice is stored in <c>Preferences</c>, which is backed by the registry on
/// Windows and NSUserDefaults on macOS, so it survives a restart, an update, and a reinstall-over.
/// </remarks>
public sealed class ThemeService : IThemeService
{
    // Bump-proof key: the stored value is the enum name, so adding a preference later can't renumber
    // what is already on disk the way persisting the integer would.
    private const string PreferenceKey = "app_theme_preference";

    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public void Set(ThemePreference preference)
    {
        Current = preference;
        Preferences.Default.Set(PreferenceKey, preference.ToString());
        Apply(preference);
    }

    public void ApplyStored()
    {
        string stored = Preferences.Default.Get(PreferenceKey, nameof(ThemePreference.System));

        // An unreadable or outdated stored value falls back to System rather than throwing — a bad
        // preference should never stop the app starting.
        Current = Enum.TryParse(stored, out ThemePreference parsed) ? parsed : ThemePreference.System;
        Apply(Current);
    }

    private static void Apply(ThemePreference preference)
    {
        if (Application.Current is null)
        {
            return;
        }

        // Unspecified is MAUI's "follow the OS" — and it keeps following, so a user on System sees the
        // app change when their desktop switches at sunset. Setting Light/Dark explicitly would freeze it.
        Application.Current.UserAppTheme = preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }
}
