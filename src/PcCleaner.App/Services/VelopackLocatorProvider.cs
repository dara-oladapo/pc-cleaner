using Velopack.Locators;
#if MACCATALYST
using Velopack.Logging;
#endif

namespace PcCleaner.App.Services;

/// <summary>
/// Supplies the Velopack locator explicitly where Velopack can't pick one itself.
/// </summary>
/// <remarks>
/// Velopack chooses its default locator from <c>OperatingSystem.IsWindows/IsLinux/IsMacOS</c>. Under Mac
/// Catalyst <c>IsMacOS()</c> is false (<c>IsMacCatalyst()</c> is true instead), so the default throws
/// <c>PlatformNotSupportedException: OS platform '' is not supported</c> and the app dies on its first line.
/// A Catalyst .app has the same bundle layout as a native macOS app (Contents/MacOS holds the executable,
/// vpk's UpdateMac and sq.version), so the macOS locator works when constructed directly.
/// </remarks>
public static class VelopackLocatorProvider
{
    private static readonly Lazy<IVelopackLocator?> s_locator = new(Create);

    /// <summary>
    /// The locator to hand Velopack, or null to let Velopack use its own default (Windows). Also null on
    /// Mac Catalyst when the macOS locator can't be built, e.g. a dev build that wasn't packed by vpk;
    /// <see cref="IsSupported"/> is then false and Velopack must not be called at all.
    /// </summary>
    public static IVelopackLocator? Locator => s_locator.Value;

    /// <summary>False when calling into Velopack would throw because there's no usable locator.</summary>
    public static bool IsSupported => !OperatingSystem.IsMacCatalyst() || Locator is not null;

    private static IVelopackLocator? Create()
    {
#if MACCATALYST
        try
        {
            var log = new NullVelopackLogger();
            // CA1416: Velopack marks this type [SupportedOSPlatform("macos")], which the analyzer treats as
            // excluding Mac Catalyst. Using it here on purpose is the whole point of this class.
#pragma warning disable CA1416
            return new OsxVelopackLocator(new DefaultProcessImpl(log), log);
#pragma warning restore CA1416
        }
        catch (Exception)
        {
            return null;
        }
#else
        return null;
#endif
    }
}
