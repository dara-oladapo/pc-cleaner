using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

public class TrashInfoFileTests
{
    [Fact]
    public void Format_ProducesSpecLayout_WithLfLineEndings()
    {
        string info = TrashInfoFile.Format("/home/user/Documents/report.pdf", new DateTime(2004, 8, 31, 22, 32, 8));

        Assert.Equal(
            "[Trash Info]\nPath=/home/user/Documents/report.pdf\nDeletionDate=2004-08-31T22:32:08\n",
            info);
    }

    [Fact]
    public void Format_DateIsCultureInvariantAndHasNoFractionOrZone()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A culture with a non-Gregorian calendar and different separators must not leak into the file.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            string info = TrashInfoFile.Format("/a", new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Local));
            Assert.EndsWith("DeletionDate=2026-01-02T03:04:05\n", info);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Format_EncodesThePath()
    {
        string info = TrashInfoFile.Format("/media/usb/My Files/a b.txt", new DateTime(2026, 1, 1));
        Assert.Contains("Path=/media/usb/My%20Files/a%20b.txt\n", info);
    }

    [Theory]
    [InlineData("/home/user/file.txt", "/home/user/file.txt")]
    [InlineData("relative/dir/file", "relative/dir/file")]
    [InlineData("/a b", "/a%20b")]
    [InlineData("/100%", "/100%25")]
    [InlineData("/café", "/caf%C3%A9")]
    [InlineData("/\U0001F600", "/%F0%9F%98%80")]
    [InlineData("/a-b_c.d~e", "/a-b_c.d~e")]
    [InlineData("/x?y#z&w=v+u", "/x%3Fy%23z%26w%3Dv%2Bu")]
    [InlineData("/back\\slash", "/back%5Cslash")]
    [InlineData("/new\nline", "/new%0Aline")]
    [InlineData("/(paren)'s!*", "/%28paren%29%27s%21%2A")]
    public void EncodePath_MatchesGio(string path, string expected)
    {
        Assert.Equal(expected, TrashInfoFile.EncodePath(path));
    }

    [Theory]
    [InlineData("/home/user/a b/c%d/café/\U0001F600.txt")]
    [InlineData("rel/path with spaces")]
    [InlineData("/~!@#$%^&*()_+{}|:\"<>?[];',.")]
    public void DecodePath_RoundTripsEncodePath(string path)
    {
        Assert.Equal(path, TrashInfoFile.DecodePath(TrashInfoFile.EncodePath(path)));
    }

    [Fact]
    public void DecodePath_AcceptsLowerCaseHexAndUnescapedReservedCharacters()
    {
        Assert.Equal("/café (1)", TrashInfoFile.DecodePath("/caf%c3%a9%20(1)"));
    }

    [Theory]
    [InlineData("/bad%")]
    [InlineData("/bad%2")]
    [InlineData("/bad%zz")]
    public void DecodePath_RejectsBrokenEscapes(string encoded)
    {
        Assert.Throws<FormatException>(() => TrashInfoFile.DecodePath(encoded));
    }
}
