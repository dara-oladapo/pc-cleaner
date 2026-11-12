using PcCleaner.Core.Models;
using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

/// <summary>Every test gets its own temp "config home" and "system" dirs; nothing touches the real ~/.config or /etc/xdg.</summary>
public sealed class XdgAutostartStoreTests : IDisposable
{
    private const string AppEntry = "[Desktop Entry]\nType=Application\nName=App\nExec=/usr/bin/app --tray\n";

    private readonly TestDirectory _dir = new();

    private string UserAutostart => Path.Combine(_dir.Path, "home", "autostart");

    private string SystemAutostart => Path.Combine(_dir.Path, "etc", "autostart");

    private string VendorAutostart => Path.Combine(_dir.Path, "vendor", "autostart");

    public void Dispose() => _dir.Dispose();

    private XdgAutostartStore Store(string desktops = "GNOME", string? lang = null, Func<string, bool>? exists = null) =>
        new(
            new XdgEnvironment(
                Path.Combine(_dir.Path, "home"),
                [Path.Combine(_dir.Path, "vendor"), Path.Combine(_dir.Path, "etc")],
                desktops.Split(':', StringSplitOptions.RemoveEmptyEntries),
                XdgEnvironment.LocaleCandidatesFor(lang),
                []),
            exists ?? (_ => true));

    private string WriteUser(string name, string text) => _dir.WriteFile(Path.Combine("home", "autostart", name), text);

    private string WriteSystem(string name, string text) => _dir.WriteFile(Path.Combine("etc", "autostart", name), text);

    private string WriteVendor(string name, string text) => _dir.WriteFile(Path.Combine("vendor", "autostart", name), text);

    private XdgAutostartEntry Single(XdgAutostartStore store) => Assert.Single(store.List());

    [Fact]
    public void List_NoDirectories_IsEmpty()
    {
        Assert.Empty(Store().List());
    }

    [Fact]
    public void List_UserEntry_IsCurrentUserAndEnabled()
    {
        WriteUser("app.desktop", AppEntry);

        var entry = Single(Store());
        Assert.Equal("app.desktop", entry.DesktopId);
        Assert.Equal("App", entry.Name);
        Assert.Equal("/usr/bin/app --tray", entry.Exec);
        Assert.Equal("/usr/bin/app", entry.Program);
        Assert.Equal(StartupItemScope.CurrentUser, entry.Scope);
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public void List_SystemEntry_IsAllUsers()
    {
        WriteSystem("app.desktop", AppEntry);
        Assert.Equal(StartupItemScope.AllUsers, Single(Store()).Scope);
    }

    [Fact]
    public void List_UserFileShadowsSystemFileWithSameName()
    {
        WriteSystem("app.desktop", AppEntry);
        WriteUser("app.desktop", AppEntry + "Hidden=true\n");

        var entry = Single(Store());
        Assert.Equal(StartupItemScope.CurrentUser, entry.Scope);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void List_EarlierConfigDirShadowsLaterOne()
    {
        WriteSystem("app.desktop", AppEntry.Replace("Name=App", "Name=Etc"));
        WriteVendor("app.desktop", AppEntry.Replace("Name=App", "Name=Vendor"));

        var entry = Single(Store());
        Assert.Equal("Vendor", entry.Name);
        Assert.Equal(Path.Combine(VendorAutostart, "app.desktop"), entry.EffectivePath);
    }

    [Fact]
    public void List_HiddenStub_TakesNameAndCommandFromTheFileItHides()
    {
        WriteSystem("app.desktop", AppEntry);
        WriteUser("app.desktop", "[Desktop Entry]\nHidden=true\n");

        var entry = Single(Store());
        Assert.Equal("App", entry.Name);
        Assert.Equal("/usr/bin/app --tray", entry.Exec);
        Assert.False(entry.IsEnabled);
    }

    [Fact]
    public void List_UsesLocalizedName()
    {
        WriteUser("app.desktop", AppEntry + "Name[de]=Anwendung\n");
        Assert.Equal("Anwendung", Single(Store(lang: "de_DE.UTF-8")).Name);
        Assert.Equal("App", Single(Store(lang: "fr_FR.UTF-8")).Name);
    }

    [Fact]
    public void List_SkipsNonApplicationsAndEntriesWithoutExec()
    {
        WriteUser("link.desktop", "[Desktop Entry]\nType=Link\nName=L\nURL=https://example.com\nExec=x\n");
        WriteUser("noexec.desktop", "[Desktop Entry]\nType=Application\nName=N\n");
        WriteUser("notes.txt", AppEntry);
        Assert.Empty(Store().List());
    }

    [Fact]
    public void List_ReportsDesktopAndTryExecRules()
    {
        WriteUser("kde.desktop", AppEntry + "OnlyShowIn=KDE;\n");
        WriteUser("gone.desktop", AppEntry + "TryExec=gone-app\n");
        WriteUser("gnome-off.desktop", AppEntry + "X-GNOME-Autostart-enabled=false\n");

        var entries = Store(exists: p => p != "gone-app").List().ToDictionary(e => e.DesktopId);
        Assert.Equal(AutostartInactiveReasons.NotShownInCurrentDesktop, entries["kde.desktop"].InactiveReasons);
        Assert.Equal(AutostartInactiveReasons.TryExecMissing, entries["gone.desktop"].InactiveReasons);
        Assert.Equal(AutostartInactiveReasons.GnomeAutostartDisabled, entries["gnome-off.desktop"].InactiveReasons);
    }

    [Fact]
    public void DisableUserEntry_SetsHiddenTrue_AsAMinimalEdit()
    {
        const string original = "# my comment\n[Desktop Entry]\nType=Application\nName=App\nExec=app\n# after\n\n[Desktop Action x]\nExec=y\n";
        string path = WriteUser("app.desktop", original);

        Store().SetEnabled("app.desktop", false);

        Assert.Equal(
            "# my comment\n[Desktop Entry]\nType=Application\nName=App\nExec=app\nHidden=true\n# after\n\n[Desktop Action x]\nExec=y\n",
            File.ReadAllText(path));
        Assert.False(Single(Store()).IsEnabled);
        Assert.Single(Directory.GetFiles(UserAutostart)); // no temp files left behind
    }

    [Fact]
    public void DisableThenEnableUserEntry_RestoresTheOriginalBytes()
    {
        string path = WriteUser("app.desktop", AppEntry);

        Store().SetEnabled("app.desktop", false);
        Store().SetEnabled("app.desktop", true);

        Assert.Equal(AppEntry, File.ReadAllText(path));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void EnableUserEntry_TurnsGnomeAutostartBackOn()
    {
        string path = WriteUser("app.desktop", AppEntry + "X-GNOME-Autostart-enabled=false\n");

        Store().SetEnabled("app.desktop", true);

        Assert.Equal(AppEntry + "X-GNOME-Autostart-enabled=true\n", File.ReadAllText(path));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void DisableSystemEntry_WritesMarkedShadowCopy_AndLeavesSystemFileAlone()
    {
        string system = WriteSystem("app.desktop", AppEntry);

        Store().SetEnabled("app.desktop", false);

        Assert.Equal(AppEntry, File.ReadAllText(system));
        string shadow = Path.Combine(UserAutostart, "app.desktop");
        Assert.Equal(AppEntry + "Hidden=true\nX-PcCleaner-Shadow=true\n", File.ReadAllText(shadow));

        var entry = Single(Store());
        Assert.False(entry.IsEnabled);
        Assert.Equal(StartupItemScope.CurrentUser, entry.Scope);
    }

    [Fact]
    public void EnableUntouchedShadow_DeletesIt_RevealingTheSystemEntry()
    {
        WriteSystem("app.desktop", AppEntry);
        Store().SetEnabled("app.desktop", false);

        Store().SetEnabled("app.desktop", true);

        Assert.False(File.Exists(Path.Combine(UserAutostart, "app.desktop")));
        var entry = Single(Store());
        Assert.True(entry.IsEnabled);
        Assert.Equal(StartupItemScope.AllUsers, entry.Scope);
    }

    [Fact]
    public void EnableEditedShadow_KeepsTheFile_AndOnlyRemovesHidden()
    {
        WriteSystem("app.desktop", AppEntry);
        Store().SetEnabled("app.desktop", false);
        string shadow = Path.Combine(UserAutostart, "app.desktop");
        File.AppendAllText(shadow, "X-User-Tweak=1\n");

        Store().SetEnabled("app.desktop", true);

        Assert.Equal(AppEntry + "X-PcCleaner-Shadow=true\nX-User-Tweak=1\n", File.ReadAllText(shadow));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void EnableUnmarkedUserCopy_IsNeverDeleted_EvenIfIdenticalToAShadow()
    {
        WriteSystem("app.desktop", AppEntry);
        string user = WriteUser("app.desktop", AppEntry + "Hidden=true\n");

        Store().SetEnabled("app.desktop", true);

        Assert.Equal(AppEntry, File.ReadAllText(user));
    }

    [Fact]
    public void EnableShadow_AfterSystemFileChanged_EditsInsteadOfDeleting()
    {
        string system = WriteSystem("app.desktop", AppEntry);
        Store().SetEnabled("app.desktop", false);
        File.WriteAllText(system, AppEntry + "Comment=updated by package\n");

        Store().SetEnabled("app.desktop", true);

        string shadow = Path.Combine(UserAutostart, "app.desktop");
        Assert.True(File.Exists(shadow));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void EnableHiddenStub_DeletesTheStub()
    {
        WriteSystem("app.desktop", AppEntry);
        string stub = WriteUser("app.desktop", "[Desktop Entry]\nHidden=true\n");

        Store().SetEnabled("app.desktop", true);

        Assert.False(File.Exists(stub));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void EnableSystemEntryDisabledByVendor_WritesEnabledUserCopy()
    {
        string system = WriteSystem("app.desktop", AppEntry + "X-GNOME-Autostart-enabled=false\n");

        Store().SetEnabled("app.desktop", true);

        Assert.Equal(AppEntry + "X-GNOME-Autostart-enabled=false\n", File.ReadAllText(system));
        Assert.Equal(AppEntry + "X-GNOME-Autostart-enabled=true\n", File.ReadAllText(Path.Combine(UserAutostart, "app.desktop")));
        Assert.True(Single(Store()).IsEnabled);
    }

    [Fact]
    public void EnableAlreadyEnabledSystemEntry_WritesNothing()
    {
        WriteSystem("app.desktop", AppEntry);
        Store().SetEnabled("app.desktop", true);
        Assert.False(Directory.Exists(UserAutostart));
    }

    [Fact]
    public void Enable_BlockedByOnlyShowIn_ThrowsAndChangesNothing()
    {
        string path = WriteUser("app.desktop", AppEntry + "OnlyShowIn=KDE;\nHidden=true\n");

        var ex = Assert.Throws<InvalidOperationException>(() => Store("GNOME").SetEnabled("app.desktop", true));
        Assert.Contains("desktop environments", ex.Message);
        Assert.Equal(AppEntry + "OnlyShowIn=KDE;\nHidden=true\n", File.ReadAllText(path));
    }

    [Fact]
    public void Enable_BlockedByMissingTryExec_Throws()
    {
        WriteUser("app.desktop", AppEntry + "TryExec=gone\nHidden=true\n");
        Assert.Throws<InvalidOperationException>(() => Store(exists: _ => false).SetEnabled("app.desktop", true));
    }

    [Fact]
    public void Disable_IsIdempotent()
    {
        string path = WriteUser("app.desktop", AppEntry);
        Store().SetEnabled("app.desktop", false);
        Store().SetEnabled("app.desktop", false);
        Assert.Equal(AppEntry + "Hidden=true\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("../evil.desktop")]
    [InlineData("app")]
    [InlineData("")]
    [InlineData("sub/app.desktop")]
    public void SetEnabled_RejectsNamesThatAreNotPlainDesktopFiles(string id)
    {
        Assert.Throws<ArgumentException>(() => Store().SetEnabled(id, false));
    }

    [Fact]
    public void SetEnabled_UnknownEntry_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => Store().SetEnabled("missing.desktop", false));
    }

    [Fact]
    public void SetEnabled_NoUserConfigHome_Throws()
    {
        WriteSystem("app.desktop", AppEntry);
        var store = new XdgAutostartStore(new XdgEnvironment(null, [Path.Combine(_dir.Path, "etc")], [], [], []));
        Assert.Throws<InvalidOperationException>(() => store.SetEnabled("app.desktop", false));
    }

    [Fact]
    public void DefaultTryExecCheck_SearchesPath()
    {
        string bin = Path.Combine(_dir.Path, "bin");
        _dir.WriteFile(Path.Combine("bin", "present-tool"), "#!/bin/sh\n");
        WriteUser("a.desktop", AppEntry + "TryExec=present-tool\n");
        WriteUser("b.desktop", AppEntry + "TryExec=absent-tool\n");
        WriteUser("c.desktop", AppEntry + $"TryExec={Path.Combine(bin, "present-tool")}\n");

        var store = new XdgAutostartStore(new XdgEnvironment(Path.Combine(_dir.Path, "home"), [], [], [], [bin]));
        var entries = store.List().ToDictionary(e => e.DesktopId, e => e.IsEnabled);

        Assert.True(entries["a.desktop"]);
        Assert.False(entries["b.desktop"]);
        Assert.True(entries["c.desktop"]);
    }

    [LinuxOnlyFact]
    public void Disable_KeepsTheFilesUnixMode()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string path = WriteUser("app.desktop", AppEntry);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);

        Store().SetEnabled("app.desktop", false);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead,
            File.GetUnixFileMode(path));
    }

    [LinuxOnlyFact]
    public void ShadowCopy_NeverGetsGroupOrOtherWrite()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string system = WriteSystem("app.desktop", AppEntry);
        File.SetUnixFileMode(system, (UnixFileMode)0b110_110_110); // 0666

        Store().SetEnabled("app.desktop", false);

        var mode = File.GetUnixFileMode(Path.Combine(UserAutostart, "app.desktop"));
        Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
    }
}
