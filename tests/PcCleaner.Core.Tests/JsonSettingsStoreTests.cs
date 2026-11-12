using System.Text.Json.Nodes;
using PcCleaner.Core.Settings;

namespace PcCleaner.Core.Tests;

public class JsonSettingsStoreTests
{
    private enum Shade
    {
        System,
        Light,
        Dark,
    }

    private const string Key = "app_theme_preference";

    [Fact]
    public void GetEnum_MissingFile_ReturnsFallback()
    {
        using var dir = new TestDirectory();
        var store = new JsonSettingsStore(Path.Combine(dir.Path, "pc-cleaner", "settings.json"));

        Assert.Equal(Shade.System, store.GetEnum(Key, Shade.System));
        Assert.Null(store.GetString(Key));
    }

    [Fact]
    public void SetEnum_RoundTripsThroughANewInstance()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "pc-cleaner", "settings.json");

        Assert.True(new JsonSettingsStore(path).SetEnum(Key, Shade.Dark));

        Assert.Equal(Shade.Dark, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Fact]
    public void SetEnum_StoresTheNameNotTheNumber()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "settings.json");

        new JsonSettingsStore(path).SetEnum(Key, Shade.Light);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("Light", root[Key]!.GetValue<string>());
    }

    [Fact]
    public void SetString_CreatesMissingDirectories()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "a", "b", "pc-cleaner", "settings.json");

        Assert.True(new JsonSettingsStore(path).SetString(Key, "Dark"));
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("null")]
    [InlineData("{ \"app_theme_preference\": \"Dark\", \"app_theme_preference\": \"Light\" }")]
    public void GetEnum_CorruptFile_ReturnsFallbackWithoutThrowing(string content)
    {
        using var dir = new TestDirectory();
        string path = dir.WriteFile("settings.json", content);

        Assert.Equal(Shade.System, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Theory]
    [InlineData("\"Purple\"")]
    [InlineData("\"1\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"Light, Dark\"")]
    [InlineData("2")]
    [InlineData("true")]
    [InlineData("{ \"nested\": \"Dark\" }")]
    [InlineData("null")]
    public void GetEnum_UnknownOrNonNameValue_ReturnsFallback(string jsonValue)
    {
        using var dir = new TestDirectory();
        string path = dir.WriteFile("settings.json", $"{{ \"{Key}\": {jsonValue} }}");

        Assert.Equal(Shade.System, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("dark")]
    [InlineData(" DARK ")]
    public void GetEnum_NameIgnoresCaseAndWhitespace(string stored)
    {
        using var dir = new TestDirectory();
        string path = dir.WriteFile("settings.json", $"{{ \"{Key}\": \"{stored}\" }}");

        Assert.Equal(Shade.Dark, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Fact]
    public void SetString_OverCorruptFile_ReplacesIt()
    {
        using var dir = new TestDirectory();
        string path = dir.WriteFile("settings.json", "{ garbage");

        Assert.True(new JsonSettingsStore(path).SetEnum(Key, Shade.Dark));

        Assert.Equal(Shade.Dark, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Fact]
    public void SetString_PreservesKeysItDoesNotKnowAbout()
    {
        using var dir = new TestDirectory();
        string path = dir.WriteFile("settings.json", "{ \"future_setting\": { \"a\": [1, 2] }, \"other\": 5 }");

        new JsonSettingsStore(path).SetEnum(Key, Shade.Dark);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("Dark", root[Key]!.GetValue<string>());
        Assert.Equal(5, root["other"]!.GetValue<int>());
        Assert.Equal(2, root["future_setting"]!["a"]!.AsArray().Count);
    }

    [Fact]
    public void SetString_RepeatedWrites_LeaveNoTempFilesBehind()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "settings.json");
        var store = new JsonSettingsStore(path);

        store.SetEnum(Key, Shade.Light);
        store.SetEnum(Key, Shade.Dark);
        store.SetString("another", "value");

        Assert.Equal([path], Directory.GetFiles(dir.Path));
        Assert.Equal(Shade.Dark, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }

    [Fact]
    public void SetString_WhenTargetCannotBeWritten_ReturnsFalseAndKeepsValueInMemory()
    {
        using var dir = new TestDirectory();

        // A directory where the file should be: the final rename can't replace it on any OS.
        string path = Path.Combine(dir.Path, "settings.json");
        Directory.CreateDirectory(path);
        var store = new JsonSettingsStore(path);

        Assert.False(store.SetEnum(Key, Shade.Dark));
        Assert.Equal(Shade.Dark, store.GetEnum(Key, Shade.System));
        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void GetEnum_UnreadablePath_ReturnsFallback()
    {
        using var dir = new TestDirectory();
        string path = Path.Combine(dir.Path, "settings.json");
        Directory.CreateDirectory(path);

        Assert.Equal(Shade.System, new JsonSettingsStore(path).GetEnum(Key, Shade.System));
    }
}
