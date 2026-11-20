using System.Text.Json;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// <see cref="ISettingsStore"/> over one JSON file under <c>$XDG_CONFIG_HOME/pc-cleaner</c> (or
/// <c>~/.config/pc-cleaner</c>), the Linux counterpart of MAUI's <c>Preferences</c>. A missing or unreadable file
/// reads as "never written"; a failed write keeps the value for this run.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public JsonFileSettingsStore()
        : this(DefaultPath())
    {
    }

    public JsonFileSettingsStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_values));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string DefaultPath()
    {
        string? config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string root = !string.IsNullOrEmpty(config) && Path.IsPathFullyQualified(config)
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "pc-cleaner", "settings.json");
    }
}
