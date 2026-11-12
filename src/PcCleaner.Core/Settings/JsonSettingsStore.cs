using System.Text.Json;
using System.Text.Json.Nodes;

namespace PcCleaner.Core.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> over a single flat JSON object on disk, e.g. <c>{ "app_theme_preference": "Dark" }</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is read once, lazily, and every write rewrites the whole object. Keys this build doesn't know about are
/// kept, so a newer build's settings survive a round trip through an older one.
/// </para>
/// <para>
/// Writes are atomic: the new content goes to a temp file in the same directory, which then replaces the real file
/// with a rename. A crash or power cut mid-write leaves the previous file intact rather than a truncated one.
/// </para>
/// <para>
/// A missing, unreadable, or corrupt file reads as empty — never an exception. The next successful write replaces it.
/// </para>
/// </remarks>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private Dictionary<string, JsonNode?>? _values;

    public JsonSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public string FilePath { get; }

    public string? GetString(string key)
    {
        lock (_gate)
        {
            return Values.TryGetValue(key, out JsonNode? node)
                && node is JsonValue value
                && value.TryGetValue(out string? text)
                ? text
                : null;
        }
    }

    public bool SetString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            Values[key] = JsonValue.Create(value);
            return TrySave();
        }
    }

    private Dictionary<string, JsonNode?> Values => _values ??= Load();

    private Dictionary<string, JsonNode?> Load()
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(FilePath))
            {
                return values;
            }

            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root)
            {
                foreach (var (key, node) in root)
                {
                    // Detach from the parsed tree so each node can be re-parented into a fresh object on save.
                    values[key] = node?.DeepClone();
                }
            }
        }
        catch (Exception)
        {
            // Any failure, not a filtered list: besides IO and JSON errors, a hand-edited file can trip others (a
            // duplicate key is an ArgumentException). Corrupt or unreadable: behave as if there were no settings. Every getter then returns its default.
            values.Clear();
        }

        return values;
    }

    private bool TrySave()
    {
        string? tempPath = null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
            Directory.CreateDirectory(directory);

            var root = new JsonObject();
            foreach (var (key, node) in Values)
            {
                root[key] = node?.DeepClone();
            }

            // Same directory as the target, so the rename below stays on one filesystem and is atomic.
            tempPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, root, WriteOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            tempPath = null;
            return true;
        }
        catch (Exception)
        {
            // Read-only home, full disk, a directory where the file should be: the caller keeps the in-memory value.
            return false;
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
