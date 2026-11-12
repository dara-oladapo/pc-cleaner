using System.Text;
using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

public class TrashNameGeneratorTests
{
    [Fact]
    public void Candidates_StartWithOriginalName_ThenNumberBeforeExtension()
    {
        var names = TrashNameGenerator.Candidates("report.pdf").Take(4).ToList();
        Assert.Equal(["report.pdf", "report.2.pdf", "report.3.pdf", "report.4.pdf"], names);
    }

    [Theory]
    [InlineData(".bashrc", ".bashrc.2")]          // Leading dot is the name, not an extension.
    [InlineData("Makefile", "Makefile.2")]
    [InlineData("archive.tar.gz", "archive.tar.2.gz")]
    [InlineData("trailing.", "trailing..2")]
    [InlineData("a.thisisaverylongextension", "a.thisisaverylongextension.2")]
    public void Candidates_SecondName(string original, string expectedSecond)
    {
        Assert.Equal(expectedSecond, TrashNameGenerator.Candidates(original).ElementAt(1));
    }

    [Fact]
    public void Candidates_AreAllDistinct()
    {
        var names = TrashNameGenerator.Candidates("x.txt").Take(500).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Candidates_AreFinite()
    {
        Assert.Equal(TrashNameGenerator.MaxAttempts, TrashNameGenerator.Candidates("x").Count());
    }

    [Fact]
    public void Candidates_LongName_LeavesRoomForTrashinfoSuffix()
    {
        string longName = new string('a', 250) + ".mkv"; // 254 bytes: legal on disk, but + ".trashinfo" isn't.

        foreach (string name in TrashNameGenerator.Candidates(longName).Take(20))
        {
            int bytes = Encoding.UTF8.GetByteCount(name);
            Assert.True(bytes <= TrashNameGenerator.MaxNameBytes, $"{bytes} bytes");
            Assert.True(bytes + TrashInfoFile.Extension.Length <= 255);
            Assert.EndsWith(".mkv", name);
        }
    }

    [Fact]
    public void Candidates_LongMultiByteName_IsNotCutMidCharacter()
    {
        string longName = string.Concat(Enumerable.Repeat("é", 127)) + ".txt"; // 254 + 4 bytes.

        string first = TrashNameGenerator.Candidates(longName).First();

        Assert.DoesNotContain('�', first);
        Assert.True(Encoding.UTF8.GetByteCount(first) <= TrashNameGenerator.MaxNameBytes);
        Assert.EndsWith(".txt", first);
        Assert.Equal(first, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(first)));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void Candidates_RejectsNonComponents(string bad)
    {
        Assert.ThrowsAny<ArgumentException>(() => TrashNameGenerator.Candidates(bad).First());
    }
}
