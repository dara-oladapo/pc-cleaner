using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class DesktopEntryFileTests
{
    private const string Sample =
        "# Written by hand\n" +
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        "Name=Sync\n" +
        "Name[de]=Synchronisieren\n" +
        "Exec=/usr/bin/sync-app --tray\n" +
        "# trailing comment in group\n" +
        "\n" +
        "[Desktop Action quit]\n" +
        "Name=Quit\n" +
        "Exec=sync-app --quit\n";

    [Theory]
    [InlineData(Sample)]
    [InlineData("[Desktop Entry]\r\nName=A\r\n# c\r\n")]
    [InlineData("[Desktop Entry]\nName=A")]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("[Desktop Entry]\r\nName=A\nExec=b\r\n")]
    public void ParseThenToText_RoundTripsExactly(string text)
    {
        Assert.Equal(text, DesktopEntryFile.Parse(text).ToText());
    }

    [Fact]
    public void Get_ReadsOnlyFromRequestedGroup()
    {
        var file = DesktopEntryFile.Parse(Sample);

        Assert.Equal("/usr/bin/sync-app --tray", file.GetString("Exec"));
        Assert.Equal("sync-app --quit", file.GetString("Exec", "Desktop Action quit"));
        Assert.Equal("Synchronisieren", file.GetString("Name[de]"));
        Assert.Null(file.GetString("Hidden"));
    }

    [Fact]
    public void Get_TrimsSpacesAroundEqualsAndLastDuplicateWins()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nHidden = false\nHidden= true  \n");
        Assert.True(file.GetBoolean("Hidden"));
    }

    [Theory]
    [InlineData("a\\sb", "a b")]
    [InlineData("a\\nb", "a\nb")]
    [InlineData("a\\\\b", "a\\b")]
    [InlineData("a\\qb", "a\\qb")]
    [InlineData("trailing\\", "trailing\\")]
    public void Unescape_DecodesSpecEscapes(string raw, string expected)
    {
        Assert.Equal(expected, DesktopEntryFile.Unescape(raw));
    }

    [Fact]
    public void GetList_SplitsOnSemicolonAndHonoursEscapedSemicolon()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nOnlyShowIn=GNOME;X-Odd\\;Name;;KDE;\n");
        Assert.Equal(["GNOME", "X-Odd;Name", "KDE"], file.GetList("OnlyShowIn"));
    }

    [Fact]
    public void GetBoolean_RejectsAnythingButTrueOrFalse()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nHidden=True\nX-GNOME-Autostart-enabled=false\n");
        Assert.Null(file.GetBoolean("Hidden"));
        Assert.False(file.GetBoolean("X-GNOME-Autostart-enabled"));
    }

    [Fact]
    public void Set_NewKey_InsertedAfterLastKeyOfGroup_LeavingCommentsAndOtherGroupsAlone()
    {
        var file = DesktopEntryFile.Parse(Sample);

        Assert.True(file.Set("Hidden", "true"));

        string expected = Sample.Replace(
            "Exec=/usr/bin/sync-app --tray\n",
            "Exec=/usr/bin/sync-app --tray\nHidden=true\n");
        Assert.Equal(expected, file.ToText());
    }

    [Fact]
    public void Set_ExistingKey_EditsThatLineOnlyAndKeepsItsSpacing()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nName=A\nHidden = false\nExec=a\n");

        Assert.True(file.Set("Hidden", "true"));
        Assert.Equal("[Desktop Entry]\nName=A\nHidden = true\nExec=a\n", file.ToText());

        Assert.False(file.Set("Hidden", "true"));
    }

    [Fact]
    public void Set_KeepsCrLfLineEndings()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\r\nName=A\r\nHidden=false\r\n");
        file.Set("Hidden", "true");
        file.Set("X-New", "1");
        Assert.Equal("[Desktop Entry]\r\nName=A\r\nHidden=true\r\nX-New=1\r\n", file.ToText());
    }

    [Fact]
    public void Set_FileWithoutFinalNewline_AppendsCleanly()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nName=A");
        file.Set("Hidden", "true");
        Assert.Equal("[Desktop Entry]\nName=A\nHidden=true", file.ToText());
    }

    [Fact]
    public void Set_MissingGroup_IsAppended()
    {
        var file = DesktopEntryFile.Parse("# only a comment\n");
        file.Set("Hidden", "true");
        Assert.Equal("# only a comment\n\n[Desktop Entry]\nHidden=true\n", file.ToText());
    }

    [Fact]
    public void Set_RejectsLineBreaksAndBadKeys()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\n");
        Assert.Throws<ArgumentException>(() => file.Set("Name", "a\nExec=evil"));
        Assert.Throws<ArgumentException>(() => file.Set("Exec=x", "y"));
    }

    [Fact]
    public void Remove_DeletesOnlyThatKeysLines()
    {
        var file = DesktopEntryFile.Parse("[Desktop Entry]\nName=A\nHidden=true\n# keep\nExec=a\n[Other]\nHidden=true\n");

        Assert.True(file.Remove("Hidden"));
        Assert.Equal("[Desktop Entry]\nName=A\n# keep\nExec=a\n[Other]\nHidden=true\n", file.ToText());
        Assert.False(file.Remove("Hidden"));
    }

    [Fact]
    public void GetKeys_ListsMainGroupKeysInOrder()
    {
        Assert.Equal(["Type", "Name", "Name[de]", "Exec"], DesktopEntryFile.Parse(Sample).GetKeys());
    }
}
