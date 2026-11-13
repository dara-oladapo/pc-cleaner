using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>Recognises the provider behind a Windows sync root from its registration.</summary>
/// <remarks>
/// Windows sync root ids have the form <c>ProviderName!UserSid!AccountName</c>, for example
/// <c>OneDrive!S-1-5-21-…!Personal|e66c…</c>. The provider name is chosen by the provider, so matching is by
/// the names the known providers use, with the display name as a fallback.
/// </remarks>
public static class CloudProviderClassifier
{
    /// <summary>
    /// Providers whose roots are not cloud storage and are never listed. Phone Link's "CrossDevice" roots
    /// mirror a phone's storage: the "cloud" copy is a phone that may be switched off.
    /// </summary>
    private static readonly string[] NotCloudStorage = ["CrossDevice"];

    public static bool IsCloudStorage(string syncRootId) =>
        !NotCloudStorage.Contains(ProviderName(syncRootId), StringComparer.OrdinalIgnoreCase);

    public static CloudProvider Classify(string syncRootId, string? displayName)
    {
        string provider = ProviderName(syncRootId);
        return Match(provider) is { } fromId && fromId != CloudProvider.Other
            ? fromId
            : Match(displayName ?? string.Empty) ?? CloudProvider.Other;
    }

    /// <summary>The part of a sync root id before the first '!'.</summary>
    public static string ProviderName(string syncRootId)
    {
        int bang = syncRootId.IndexOf('!', StringComparison.Ordinal);
        return bang < 0 ? syncRootId : syncRootId[..bang];
    }

    public static string Label(CloudProvider provider) => provider switch
    {
        CloudProvider.OneDrive => "OneDrive",
        CloudProvider.GoogleDrive => "Google Drive",
        CloudProvider.Dropbox => "Dropbox",
        CloudProvider.ICloudDrive => "iCloud Drive",
        CloudProvider.Box => "Box",
        CloudProvider.SynologyDrive => "Synology Drive",
        _ => "Other cloud storage",
    };

    private static CloudProvider? Match(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.OneDrive;
        }

        if (text.Contains("GoogleDrive", StringComparison.OrdinalIgnoreCase) || text.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.GoogleDrive;
        }

        if (text.Contains("Dropbox", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.Dropbox;
        }

        if (text.Contains("iCloud", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.ICloudDrive;
        }

        if (text.Contains("Synology", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.SynologyDrive;
        }

        // "Box" is a common substring ("Dropbox", "Sandbox", "Inbox"), so only the whole provider name counts.
        if (text.Equals("Box", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Box ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("BoxDrive", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Box.", StringComparison.OrdinalIgnoreCase))
        {
            return CloudProvider.Box;
        }

        return CloudProvider.Other;
    }
}
