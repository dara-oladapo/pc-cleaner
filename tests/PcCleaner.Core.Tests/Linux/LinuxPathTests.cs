using PcCleaner.Core.Platform.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class LinuxPathTests
{
    [Theory]
    [InlineData("/home/ada/", "/home/ada")]
    [InlineData("//home//ada", "/home/ada")]
    [InlineData("/", "/")]
    [InlineData("rel//x/", "rel/x")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, LinuxPath.Normalize(input));

    [Theory]
    [InlineData("/home/ada/.cache", "/home/ada", true)]
    [InlineData("/home/ada", "/home/ada", true)]
    [InlineData("/home/adam", "/home/ada", false)] // prefix of the name, not of the path
    [InlineData("/home/ADA/x", "/home/ada", false)] // Linux paths are case-sensitive
    [InlineData("/anything", "/", true)]
    public void IsSameOrUnder(string path, string root, bool expected) =>
        Assert.Equal(expected, LinuxPath.IsSameOrUnder(path, root));

    [Fact]
    public void GetParent_And_Relative()
    {
        Assert.Equal("/home", LinuxPath.GetParent("/home/ada"));
        Assert.Equal("/", LinuxPath.GetParent("/home"));
        Assert.Null(LinuxPath.GetParent("/"));
        Assert.Equal("mozilla/firefox", LinuxPath.GetRelative("/home/ada/.cache/mozilla/firefox", "/home/ada/.cache"));
        Assert.Equal("/home/ada/.local/x", LinuxPath.Join("/home/ada/", "/.local//x"));
    }
}
