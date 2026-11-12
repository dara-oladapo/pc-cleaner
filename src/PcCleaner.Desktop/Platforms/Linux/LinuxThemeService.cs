using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Stub until #13. Remembers the choice for this session only and applies nothing.</summary>
/// <remarks>
/// The app starts on Avalonia's <c>ThemeVariant.Default</c> (set in App.axaml), which follows the desktop —
/// the right behaviour for the default <see cref="ThemePreference.System"/>. Applying Light/Dark and persisting
/// the choice under <c>$XDG_CONFIG_HOME</c> is the real implementation's job.
/// </remarks>
public sealed class LinuxThemeService : IThemeService
{
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public void Set(ThemePreference preference) => Current = preference;

    public void ApplyStored()
    {
    }
}
