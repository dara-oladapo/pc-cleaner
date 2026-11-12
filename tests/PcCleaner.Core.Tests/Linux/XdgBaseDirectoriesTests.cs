using PcCleaner.Core.Platform.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class XdgBaseDirectoriesTests
{
    private static Func<string, string?> Env(params (string Name, string? Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.TryGetValue(name, out string? value) ? value : null;
    }

    [Fact]
    public void Resolve_UsesSpecDefaults_WhenNothingIsSet()
    {
        var xdg = XdgBaseDirectories.Resolve(Env(), "/home/ada");

        Assert.Equal("/home/ada/.cache", xdg.CacheHome);
        Assert.Equal("/home/ada/.config", xdg.ConfigHome);
        Assert.Equal("/home/ada/.local/state", xdg.StateHome);
        Assert.Equal("/home/ada/.local/share", xdg.DataHome);
    }

    [Fact]
    public void Resolve_HonoursAbsoluteOverrides()
    {
        var xdg = XdgBaseDirectories.Resolve(
            Env(("XDG_CACHE_HOME", "/fast/cache"), ("XDG_CONFIG_HOME", "/cfg"), ("XDG_STATE_HOME", "/st"), ("XDG_DATA_HOME", "/data/")),
            "/home/ada");

        Assert.Equal("/fast/cache", xdg.CacheHome);
        Assert.Equal("/cfg", xdg.ConfigHome);
        Assert.Equal("/st", xdg.StateHome);
        Assert.Equal("/data", xdg.DataHome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/cache")]
    [InlineData("~/.cache")]
    [InlineData("$HOME/.cache")]
    public void Resolve_FallsBack_WhenOverrideIsEmptyOrRelative(string value)
    {
        // The spec: "If an implementation encounters a relative path in any of these variables it should consider
        // the path invalid and ignore it." No shell expansion happens either: "~" and "$HOME" are relative.
        var xdg = XdgBaseDirectories.Resolve(Env(("XDG_CACHE_HOME", value)), "/home/ada");

        Assert.Equal("/home/ada/.cache", xdg.CacheHome);
    }

    [Fact]
    public void Resolve_EachVariableFallsBackIndependently()
    {
        var xdg = XdgBaseDirectories.Resolve(Env(("XDG_STATE_HOME", "/var/state/ada")), "/home/ada/");

        Assert.Equal("/home/ada/.cache", xdg.CacheHome);
        Assert.Equal("/var/state/ada", xdg.StateHome);
    }

    [Fact]
    public void Resolve_NormalizesDuplicateSlashes()
    {
        var xdg = XdgBaseDirectories.Resolve(Env(("XDG_CACHE_HOME", "//mnt//cache//")), "/home//ada");

        Assert.Equal("/mnt/cache", xdg.CacheHome);
        Assert.Equal("/home/ada/.config", xdg.ConfigHome);
    }
}
