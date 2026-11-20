using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class SystemctlOutputTests
{
    [Fact]
    public void ListUnitFilesJson_ParsesRows()
    {
        const string json = """
            [{"unit_file":"pipewire.service","state":"enabled","preset":"enabled"},
             {"unit_file":"dbus.service","state":"static","preset":null},
             {"unit_file":"weird"}]
            """;

        var rows = SystemctlOutput.TryParseListUnitFilesJson(json);

        Assert.NotNull(rows);
        Assert.Equal([new UnitFileRow("pipewire.service", "enabled"), new UnitFileRow("dbus.service", "static")], rows);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"unit_file\":\"x\"}")]
    [InlineData("")]
    public void ListUnitFilesJson_InvalidOrNonArray_IsNull(string text)
    {
        Assert.Null(SystemctlOutput.TryParseListUnitFilesJson(text));
    }

    [Fact]
    public void ListUnitFilesText_ParsesColumns_IgnoringHeaderAndFooter()
    {
        const string text =
            "UNIT FILE                     STATE    PRESET\n" +
            "pipewire.service              enabled  enabled\n" +
            "syncthing.service             disabled enabled\n" +
            "old.service                   linked\n" +
            "\n" +
            "3 unit files listed.\n";

        Assert.Equal(
            [
                new UnitFileRow("pipewire.service", "enabled"),
                new UnitFileRow("syncthing.service", "disabled"),
                new UnitFileRow("old.service", "linked"),
            ],
            SystemctlOutput.ParseListUnitFilesText(text));
    }

    [Theory]
    [InlineData("enabled", true)]
    [InlineData("disabled", true)]
    [InlineData("linked", true)]
    [InlineData("static", false)]
    [InlineData("generated", false)]
    [InlineData("indirect", false)]
    [InlineData("alias", false)]
    [InlineData("masked", false)]
    [InlineData("enabled-runtime", false)]
    [InlineData("transient", false)]
    [InlineData("bad", false)]
    public void ToggleableStates(string state, bool expected)
    {
        Assert.Equal(expected, SystemctlOutput.IsToggleableState(state));
    }

    [Fact]
    public void Templates_AreRecognised()
    {
        Assert.True(SystemctlOutput.IsTemplate("foo@.service"));
        Assert.False(SystemctlOutput.IsTemplate("foo@bar.service"));
        Assert.False(SystemctlOutput.IsTemplate("foo.service"));
    }

    [Fact]
    public void Show_ParsesBlocksPerUnit()
    {
        const string text =
            "Id=a.service\nDescription=Thing A\nExecStart={ path=/usr/bin/a ; argv[]=/usr/bin/a -x ; ignore_errors=no }\nFragmentPath=/usr/lib/systemd/user/a.service\n" +
            "\n" +
            "Id=b.service\nDescription=Equals = inside\nExecStart=\nFragmentPath=/home/u/.config/systemd/user/b.service\n";

        var blocks = SystemctlOutput.ParseShow(text);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Thing A", blocks[0]["Description"]);
        Assert.Equal("Equals = inside", blocks[1]["Description"]);
        Assert.Equal(string.Empty, blocks[1]["ExecStart"]);
    }

    [Theory]
    [InlineData("{ path=/usr/bin/a ; argv[]=/usr/bin/a -x --y ; ignore_errors=no ; start_time=[n/a] }", "/usr/bin/a -x --y")]
    [InlineData("{ path=/usr/bin/a ; argv[]=/usr/bin/a ; ignore_errors=no } { path=/usr/bin/b ; argv[]=/usr/bin/b ; }", "/usr/bin/a")]
    [InlineData("{ path=/usr/bin/only ; ignore_errors=no }", "/usr/bin/only")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExecStart_ExtractsFirstCommand(string? value, string? expected)
    {
        Assert.Equal(expected, SystemctlOutput.ParseExecStartCommand(value));
    }

    [Fact]
    public void IsEnabled_ReadsFirstLine()
    {
        Assert.Equal("disabled", SystemctlOutput.ParseIsEnabled("\ndisabled\n"));
        Assert.Equal(string.Empty, SystemctlOutput.ParseIsEnabled(""));
    }
}
