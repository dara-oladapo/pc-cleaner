namespace PcCleaner.Core.Settings;

/// <summary>A small persisted key/value store for user preferences. Values are strings; see <see cref="SettingsStoreExtensions"/> for enums.</summary>
/// <remarks>
/// Implementations must never throw from either member: a settings file that can't be read or written is an
/// inconvenience (the choice won't survive a restart), never a reason for the app to fail to start or crash.
/// </remarks>
public interface ISettingsStore
{
    /// <summary>The stored value, or null if the key is missing, isn't a string, or the store couldn't be read.</summary>
    string? GetString(string key);

    /// <summary>Stores the value and persists it. Returns false if it could not be written to disk; the in-memory value is kept either way.</summary>
    bool SetString(string key, string value);
}

public static class SettingsStoreExtensions
{
    /// <summary>
    /// Reads an enum stored by <em>name</em>. Missing, unknown, or numeric values return <paramref name="fallback"/> —
    /// numbers are rejected on purpose, so a value written by hand (or by an older build that stored integers)
    /// can't be silently reinterpreted after the enum gains a member.
    /// </summary>
    public static TEnum GetEnum<TEnum>(this ISettingsStore store, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        string? stored = store.GetString(key);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return fallback;
        }

        foreach (string name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, stored.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        return fallback;
    }

    /// <summary>Stores an enum by name, never by its integer value, so adding a member later can't renumber what is on disk.</summary>
    public static bool SetEnum<TEnum>(this ISettingsStore store, string key, TEnum value)
        where TEnum : struct, Enum
        => store.SetString(key, value.ToString());
}
