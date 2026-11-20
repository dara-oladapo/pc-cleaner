using PcCleaner.Core.Settings;

namespace PcCleaner.Core.Tests;

public class XdgPathsTests
{
    private static readonly string AbsoluteConfig = Path.Combine(Path.GetTempPath(), "xdg-config");
    private static readonly string AbsoluteHome = Path.Combine(Path.GetTempPath(), "home-dir");

    private static Func<string, string?> Env(string? configHome, string? home) => name => name switch
    {
        "XDG_CONFIG_HOME" => configHome,
        "HOME" => home,
        _ => null,
    };

    [Fact]
    public void SettingsFile_UsesXdgConfigHomeWhenAbsolute()
    {
        string path = XdgPaths.SettingsFile(Env(AbsoluteConfig, AbsoluteHome));

        Assert.Equal(Path.Combine(AbsoluteConfig, "pc-cleaner", "settings.json"), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/config")]
    public void SettingsFile_FallsBackToHomeDotConfig(string? configHome)
    {
        string path = XdgPaths.SettingsFile(Env(configHome, AbsoluteHome));

        Assert.Equal(Path.Combine(AbsoluteHome, ".config", "pc-cleaner", "settings.json"), path);
    }

    [Fact]
    public void ConfigHome_NoHomeVariable_UsesUserProfile()
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        Assert.Equal(expected, XdgPaths.ConfigHome(Env(null, null)));
    }
}
