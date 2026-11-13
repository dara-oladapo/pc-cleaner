using PcCleaner.Core.Abstractions;

namespace PcCleaner.Core.DevCleanup;

/// <summary>
/// The single entry point to developer cleanup, and the place developer mode is enforced: while
/// <see cref="IAppSettings.DeveloperMode"/> is off it runs no provider at all, so no Docker, WSL or git
/// command can start, whatever the UI does.
/// </summary>
public sealed class DevCleanupService
{
    private readonly IReadOnlyList<IDevCleanupProvider> _providers;
    private readonly IAppSettings _settings;

    public DevCleanupService(IEnumerable<IDevCleanupProvider> providers, IAppSettings settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    public bool IsEnabled => _settings.DeveloperMode;

    /// <summary>Detects and measures every provider. Empty while developer mode is off.</summary>
    public async Task<IReadOnlyList<DevCleanupReport>> MeasureAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var reports = new List<DevCleanupReport>();
        foreach (IDevCleanupProvider provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Checking {provider.DisplayName}...");
            try
            {
                if (!await provider.DetectAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                progress?.Report($"Measuring {provider.DisplayName}...");
                reports.Add(await provider.MeasureAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A provider bug must not take the others (or the app) down with it.
                reports.Add(DevCleanupReport.Empty(provider.Id, provider.DisplayName) with { Errors = [ex.Message] });
            }
        }

        return reports;
    }

    /// <summary>Cleans the items, each by the provider that offered it. Refuses everything while developer mode is off.</summary>
    public async Task<DevCleanupResult> CleanAsync(IReadOnlyList<DevCleanupItem> items, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new DevCleanupResult([], items.Select(i => (i, "Developer mode is off.")).ToList(), 0);
        }

        var results = new List<DevCleanupResult>();
        foreach (IGrouping<string, DevCleanupItem> group in items.GroupBy(i => i.ProviderId))
        {
            IDevCleanupProvider? provider = _providers.FirstOrDefault(p => p.Id == group.Key);
            if (provider is null)
            {
                results.Add(new DevCleanupResult([], group.Select(i => (i, $"No provider '{group.Key}'.")).ToList(), 0));
                continue;
            }

            try
            {
                results.Add(await provider.CleanAsync(group.ToList(), cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new DevCleanupResult([], group.Select(i => (i, ex.Message)).ToList(), 0));
            }
        }

        return DevCleanupResult.Combine(results);
    }
}

/// <summary>The one-time suggestion to turn developer mode on. Looks for tools on disk; never runs them.</summary>
public static class DeveloperModeAdvisor
{
    /// <summary>
    /// True once, on a machine with git or docker installed, while developer mode is off and the suggestion has
    /// not been shown. The caller shows it and sets <see cref="IAppSettings.DeveloperModeSuggestionShown"/>.
    /// </summary>
    public static bool ShouldSuggest(IAppSettings settings, Func<string, bool> isInstalled) =>
        !settings.DeveloperMode
        && !settings.DeveloperModeSuggestionShown
        && (isInstalled("git") || isInstalled("docker"));
}
