using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class DesktopEntryRulesTests
{
    private static AutostartInactiveReasons Evaluate(string body, string desktops = "GNOME", Func<string, bool>? exists = null) =>
        DesktopEntryRules.Evaluate(
            DesktopEntryFile.Parse("[Desktop Entry]\nType=Application\nExec=x\n" + body),
            desktops.Split(':', StringSplitOptions.RemoveEmptyEntries),
            exists ?? (_ => true));

    [Fact]
    public void PlainEntry_IsEnabled() => Assert.Equal(AutostartInactiveReasons.None, Evaluate(""));

    [Fact]
    public void HiddenTrue_IsInactive() => Assert.Equal(AutostartInactiveReasons.Hidden, Evaluate("Hidden=true\n"));

    [Fact]
    public void HiddenFalse_IsEnabled() => Assert.Equal(AutostartInactiveReasons.None, Evaluate("Hidden=false\n"));

    [Fact]
    public void GnomeAutostartDisabled_IsInactive() =>
        Assert.Equal(AutostartInactiveReasons.GnomeAutostartDisabled, Evaluate("X-GNOME-Autostart-enabled=false\n"));

    [Theory]
    [InlineData("OnlyShowIn=KDE;\n", "GNOME", false)]
    [InlineData("OnlyShowIn=KDE;GNOME;\n", "GNOME", true)]
    [InlineData("OnlyShowIn=GNOME;\n", "ubuntu:GNOME", true)]
    [InlineData("OnlyShowIn=GNOME;\n", "", false)]
    [InlineData("OnlyShowIn=gnome;\n", "GNOME", false)]
    [InlineData("NotShowIn=GNOME;\n", "GNOME", false)]
    [InlineData("NotShowIn=GNOME;\n", "KDE", true)]
    [InlineData("NotShowIn=GNOME;\n", "", true)]
    // First desktop in XDG_CURRENT_DESKTOP that appears in either list decides.
    [InlineData("OnlyShowIn=Unity;\nNotShowIn=GNOME;\n", "Unity:GNOME", true)]
    [InlineData("OnlyShowIn=Unity;\nNotShowIn=GNOME;\n", "GNOME:Unity", false)]
    public void OnlyShowInAndNotShowIn_FollowTheSpec(string body, string desktops, bool shown)
    {
        var reasons = Evaluate(body, desktops);
        Assert.Equal(!shown, reasons.HasFlag(AutostartInactiveReasons.NotShownInCurrentDesktop));
    }

    [Fact]
    public void TryExecMissing_IsInactive()
    {
        Assert.Equal(AutostartInactiveReasons.TryExecMissing, Evaluate("TryExec=nope\n", exists: p => p != "nope"));
        Assert.Equal(AutostartInactiveReasons.None, Evaluate("TryExec=present\n", exists: p => p == "present"));
    }

    [Fact]
    public void Reasons_Combine()
    {
        var reasons = Evaluate("Hidden=true\nOnlyShowIn=KDE;\n", "GNOME");
        Assert.Equal(AutostartInactiveReasons.Hidden | AutostartInactiveReasons.NotShownInCurrentDesktop, reasons);
    }

    [Fact]
    public void LocalizedName_PicksMostSpecificMatch()
    {
        var file = DesktopEntryFile.Parse(
            "[Desktop Entry]\nName=Files\nName[sr]=Datoteke\nName[sr@latin]=Datoteke (lat)\nName[pt_BR]=Arquivos\nName[pt]=Ficheiros\n");

        Assert.Equal("Datoteke (lat)", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("sr_RS.UTF-8@latin")));
        Assert.Equal("Datoteke", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("sr_RS.UTF-8")));
        Assert.Equal("Arquivos", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("pt_BR.UTF-8")));
        Assert.Equal("Ficheiros", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("pt_PT")));
        Assert.Equal("Files", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("de_DE.UTF-8")));
        Assert.Equal("Files", DesktopEntryRules.GetLocalizedString(file, "Name", XdgEnvironment.LocaleCandidatesFor("C.UTF-8")));
    }

    [Theory]
    [InlineData("/usr/bin/foo --bar", "/usr/bin/foo")]
    [InlineData("foo", "foo")]
    [InlineData("  foo\t-x", "foo")]
    [InlineData("\"/opt/My App/app\" --min", "/opt/My App/app")]
    [InlineData("\"/opt/q\\\"uote/app\"", "/opt/q\"uote/app")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetProgram_ReturnsFirstArgument(string? exec, string? expected)
    {
        Assert.Equal(expected, DesktopEntryRules.GetProgram(exec));
    }

    [Fact]
    public void CreateShadow_AddsHiddenAndMarker_KeepingEverythingElse()
    {
        const string system = "# vendor file\n[Desktop Entry]\nType=Application\nName=Tracker\nExec=tracker-miner\n";
        Assert.Equal(
            "# vendor file\n[Desktop Entry]\nType=Application\nName=Tracker\nExec=tracker-miner\nHidden=true\nX-PcCleaner-Shadow=true\n",
            DesktopEntryRules.CreateShadow(system));
    }
}
