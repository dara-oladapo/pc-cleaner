using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class XdgEnvironmentTests
{
    // Absolute on whichever OS runs the tests.
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "xdg-env-test"));

    private static XdgEnvironment From(Dictionary<string, string> vars) =>
        XdgEnvironment.FromVariables(name => vars.TryGetValue(name, out var v) ? v : null);

    [Fact]
    public void ConfigHome_DefaultsToHomeDotConfig()
    {
        var env = From(new() { ["HOME"] = Root });
        Assert.Equal(Path.Combine(Root, ".config"), env.UserConfigHome);
        Assert.Equal(Path.Combine(Root, ".config", "autostart"), env.UserAutostartDirectory);
    }

    [Fact]
    public void ConfigHome_RelativeValueIsIgnored()
    {
        var env = From(new() { ["HOME"] = Root, ["XDG_CONFIG_HOME"] = "relative/dir" });
        Assert.Equal(Path.Combine(Root, ".config"), env.UserConfigHome);
    }

    [Fact]
    public void ConfigHome_NothingUsable_IsNull()
    {
        Assert.Null(From(new()).UserConfigHome);
        Assert.Null(From(new()).UserAutostartDirectory);
    }

    [Fact]
    public void ConfigDirs_DefaultToEtcXdg()
    {
        Assert.Equal([XdgEnvironment.DefaultConfigDir], From(new()).ConfigDirs);
    }

    [Fact]
    public void ConfigDirs_ColonSeparatedInPriorityOrder_RelativeDropped()
    {
        var env = From(new() { ["XDG_CONFIG_DIRS"] = "/etc/xdg/xdg-ubuntu:relative:/etc/xdg" });
        if (OperatingSystem.IsWindows())
        {
            // "/etc/xdg" isn't fully qualified on Windows, so everything is dropped and the default applies.
            Assert.Equal([XdgEnvironment.DefaultConfigDir], env.ConfigDirs);
        }
        else
        {
            Assert.Equal(["/etc/xdg/xdg-ubuntu", "/etc/xdg"], env.ConfigDirs);
        }
    }

    [Fact]
    public void CurrentDesktop_IsColonSeparated()
    {
        Assert.Equal(["ubuntu", "GNOME"], From(new() { ["XDG_CURRENT_DESKTOP"] = "ubuntu:GNOME" }).CurrentDesktops);
    }

    [Fact]
    public void Locale_LcAllBeatsLcMessagesBeatsLang()
    {
        Assert.Equal(["fr_FR", "fr"], From(new() { ["LANG"] = "de_DE.UTF-8", ["LC_MESSAGES"] = "fr_FR.UTF-8" }).LocaleCandidates);
        Assert.Equal(["es"], From(new() { ["LANG"] = "de_DE.UTF-8", ["LC_MESSAGES"] = "fr_FR", ["LC_ALL"] = "es" }).LocaleCandidates);
        Assert.Equal(["de_DE", "de"], From(new() { ["LANG"] = "de_DE.UTF-8" }).LocaleCandidates);
    }

    [Theory]
    [InlineData("sr_RS.UTF-8@latin", new[] { "sr_RS@latin", "sr_RS", "sr@latin", "sr" })]
    [InlineData("en_GB", new[] { "en_GB", "en" })]
    [InlineData("ja", new[] { "ja" })]
    [InlineData("C", new string[0])]
    [InlineData("POSIX", new string[0])]
    [InlineData("C.UTF-8", new string[0])]
    [InlineData("", new string[0])]
    public void LocaleCandidates_FollowSpecOrder(string locale, string[] expected)
    {
        Assert.Equal(expected, XdgEnvironment.LocaleCandidatesFor(locale));
    }
}
