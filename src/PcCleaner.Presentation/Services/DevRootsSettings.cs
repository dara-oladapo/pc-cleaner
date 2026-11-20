using System.Text.Json;
using PcCleaner.Core.DevCleanup;

namespace PcCleaner.Presentation.Services;

/// <summary>
/// The folders the Developer page searches for git repositories and projects, over <see cref="ISettingsStore"/>.
/// </summary>
/// <remarks>
/// Stored as a JSON array. "Never set" (no key) means the common conventions in
/// <see cref="DevCleanupOptions.DefaultRoots()"/>, re-evaluated each time so a folder created later is picked up;
/// once the user adds or removes one, their exact list is kept, including an empty one.
/// </remarks>
public sealed class DevRootsSettings(ISettingsStore store)
{
    public const string SettingsKey = "developer_roots";

    public IReadOnlyList<string> GetRoots() => Load() ?? DevCleanupOptions.DefaultRoots();

    public void SetRoots(IEnumerable<string> roots) =>
        store.Set(SettingsKey, JsonSerializer.Serialize(roots.Distinct().ToList()));

    /// <summary>Forgets the user's list, so the defaults apply again.</summary>
    public void Reset() => store.Set(SettingsKey, string.Empty);

    /// <summary>The options the providers read at the start of every scan and clean.</summary>
    public DevCleanupOptions CurrentOptions() => new() { Roots = GetRoots() };

    private List<string>? Load()
    {
        string? json = store.Get(SettingsKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            // An unreadable value falls back to the defaults rather than scanning nothing.
            return null;
        }
    }
}
