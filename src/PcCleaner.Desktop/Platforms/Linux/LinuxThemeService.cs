using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using PcCleaner.Core.Settings;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// <see cref="IThemeService"/> over Avalonia's <c>RequestedThemeVariant</c> and a JSON settings file — the Avalonia
/// counterpart of the MAUI app's <c>ThemeService</c> (UserAppTheme + Preferences), with the same semantics.
/// </summary>
/// <remarks>
/// <para>
/// System maps to <see cref="ThemeVariant.Default"/>, which is not a one-off sample of the desktop's theme: Avalonia
/// keeps following it. On Linux its D-Bus platform settings read <c>org.freedesktop.appearance color-scheme</c> from
/// <c>xdg-desktop-portal</c> and watch the portal's <c>SettingChanged</c> signal, and the application re-resolves
/// <c>ActualThemeVariant</c> whenever that fires while the requested variant is Default. Light or Dark freezes it.
/// </para>
/// <para>
/// The choice is stored by enum name in <c>$XDG_CONFIG_HOME/pc-cleaner/settings.json</c> (see <see cref="XdgPaths"/>).
/// An unreadable or unknown stored value falls back to System, and a failed write only means the choice won't
/// survive a restart — neither ever throws.
/// </para>
/// </remarks>
public sealed class LinuxThemeService(ISettingsStore settings) : IThemeService
{
    // Same key as the MAUI app's Preferences entry, so the two front ends describe the setting identically.
    internal const string PreferenceKey = "app_theme_preference";

    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public void Set(ThemePreference preference)
    {
        Current = preference;
        settings.SetEnum(PreferenceKey, preference);
        Apply(preference);
    }

    public void ApplyStored()
    {
        Current = settings.GetEnum(PreferenceKey, ThemePreference.System);
        Apply(Current);
    }

    internal static ThemeVariant ToVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static void Apply(ThemePreference preference)
    {
        var variant = ToVariant(preference);

        if (Dispatcher.UIThread.CheckAccess())
        {
            SetRequested(variant);
        }
        else
        {
            Dispatcher.UIThread.Post(() => SetRequested(variant));
        }
    }

    private static void SetRequested(ThemeVariant variant)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
        }
    }
}
