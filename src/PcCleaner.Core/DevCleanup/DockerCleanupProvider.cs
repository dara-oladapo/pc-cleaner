namespace PcCleaner.Core.DevCleanup;

/// <summary>A Docker engine reachable from this machine: the host CLI's, or one inside a WSL distro.</summary>
/// <param name="Distro">The WSL distro whose own <c>docker</c> CLI reaches it, or null for the host CLI.</param>
/// <param name="EngineId">Docker's engine ID, used to avoid listing one engine twice (WSL integration).</param>
public sealed record DockerEngine(string? Distro, string EngineId)
{
    public string Label => Distro is null ? "Docker (host)" : $"Docker (WSL: {Distro})";
}

/// <summary>
/// Prunes Docker build cache, unused images and stopped containers on the host engine and, on Windows, on any
/// engine running inside a WSL distro. Volumes are offered only as an explicit, warned, unselected opt-in.
/// </summary>
/// <remarks>
/// Every command is a fixed argument list passed straight to <c>docker</c> (or <c>wsl -d X --exec docker</c>,
/// which bypasses the distro's shell so Go-template braces arrive intact). Engine failures (a daemon that is
/// down, a corrupt containerd snapshot) are reported verbatim and never thrown.
/// </remarks>
public sealed class DockerCleanupProvider : IDevCleanupProvider
{
    public const string ProviderId = "docker";

    /// <summary>Prune filter: only things older than 7 days.</summary>
    public const string UntilFilter = "until=168h";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(3);

    private readonly IProcessRunner _runner;
    private readonly IWslDiskLocator _disks;
    private readonly bool _checkWsl;

    public DockerCleanupProvider(IProcessRunner runner, IWslDiskLocator disks)
        : this(runner, disks, checkWsl: OperatingSystem.IsWindows())
    {
    }

    /// <param name="checkWsl">Look for engines inside WSL distros. True on Windows; tests set it explicitly.</param>
    public DockerCleanupProvider(IProcessRunner runner, IWslDiskLocator disks, bool checkWsl)
    {
        _runner = runner;
        _disks = disks;
        _checkWsl = checkWsl;
    }

    public string Id => ProviderId;

    public string DisplayName => "Docker";

    public async Task<bool> DetectAsync(CancellationToken cancellationToken = default)
    {
        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return discovery.Engines.Count > 0 || discovery.Errors.Count > 0;
    }

    public async Task<DevCleanupReport> MeasureAsync(CancellationToken cancellationToken = default)
    {
        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<DevCleanupItem>();
        var errors = new List<string>(discovery.Errors);
        var notes = new List<string>(discovery.Notes);

        foreach (DockerEngine engine in discovery.Engines)
        {
            items.AddRange(await MeasureEngineAsync(engine, errors, cancellationToken).ConfigureAwait(false));
        }

        IReadOnlyList<VirtualDiskInfo> disks = [];
        if (_checkWsl && discovery.WslSeen)
        {
            notes.Add(
                "Pruning inside WSL frees space inside the distro's virtual disk (ext4.vhdx), but does not shrink "
                + "that file on Windows. Compacting it is a separate step that needs WSL shut down; this app does not do it.");
            disks = _disks.FindDisks();
        }

        return new DevCleanupReport(Id, DisplayName, items, notes, errors) { VirtualDisks = disks };
    }

    public async Task<DevCleanupResult> CleanAsync(IReadOnlyList<DevCleanupItem> items, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<DevCleanupItem>();
        var failed = new List<(DevCleanupItem, string)>();
        var messages = new List<string>();
        long freed = 0;

        foreach (DevCleanupItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Payload is not DockerAction action || item.ProviderId != Id)
            {
                failed.Add((item, "Not a Docker item."));
                continue;
            }

            ProcessResult result = await RunDockerAsync(action.Engine.Distro, action.Arguments, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                failed.Add((item, $"{item.Group}: {item.Title} failed: {result.ErrorText}"));
                continue;
            }

            long reclaimed = DockerOutputParser.ParseReclaimed(result.StandardOutput) ?? 0;
            freed += reclaimed;
            succeeded.Add(item);
            messages.Add($"{item.Group}: {item.Title}: reclaimed {Utilities.ByteSizeFormatter.Format(reclaimed)}.");

            // Some engines print warnings on stderr even when the prune succeeds; pass them on verbatim.
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                messages.Add($"{item.Group}: {result.StandardError.Trim()}");
            }
        }

        return new DevCleanupResult(succeeded, failed, freed) { Messages = messages };
    }

    /// <summary>The arguments each item runs. Public so the exact commands are visible to tests and reviewers.</summary>
    public static class Commands
    {
        public static readonly string[] SystemDf = ["system", "df", "--format", "{{json .}}"];
        public static readonly string[] BuilderDu = ["builder", "du"];
        public static readonly string[] EngineId = ["info", "--format", "{{.ID}}"];
        public static readonly string[] PruneBuildCache = ["builder", "prune", "-f", "--filter", UntilFilter];
        public static readonly string[] PruneImages = ["image", "prune", "-a", "-f", "--filter", UntilFilter];
        public static readonly string[] PruneContainers = ["container", "prune", "-f"];

        /// <summary>Without <c>-a</c>: on current Docker this removes only unused anonymous volumes.</summary>
        public static readonly string[] PruneVolumes = ["volume", "prune", "-f"];
    }

    private sealed record DockerAction(DockerEngine Engine, string[] Arguments);

    private sealed record Discovery(List<DockerEngine> Engines, List<string> Notes, List<string> Errors, bool WslSeen);

    private async Task<Discovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        var engines = new List<DockerEngine>();
        var notes = new List<string>();
        var errors = new List<string>();
        bool wslSeen = false;

        ProcessResult host = await ProbeAsync(null, Commands.EngineId, cancellationToken).ConfigureAwait(false);
        if (host.Succeeded && host.StandardOutput.Trim() is { Length: > 0 } hostId)
        {
            engines.Add(new DockerEngine(null, hostId));
        }
        else if (host.Started)
        {
            errors.Add($"Docker (host): {host.ErrorText}");
        }

        if (_checkWsl)
        {
            ProcessResult list = await _runner.RunAsync("wsl", ["-l", "-v"], null, cancellationToken).ConfigureAwait(false);
            if (list.Succeeded)
            {
                foreach (WslDistro distro in DockerOutputParser.ParseWslList(list.StandardOutput))
                {
                    // Docker Desktop's own distros are the host engine seen from the inside.
                    if (distro.Name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    wslSeen = true;
                    if (!distro.IsRunning)
                    {
                        notes.Add($"WSL distro '{distro.Name}' is not running; it was not started just to measure it.");
                        continue;
                    }

                    ProcessResult probe = await ProbeAsync(distro.Name, Commands.EngineId, cancellationToken).ConfigureAwait(false);
                    if (probe.Succeeded && probe.StandardOutput.Trim() is { Length: > 0 } id)
                    {
                        // WSL integration makes Docker Desktop's engine visible inside a distro too; count it once.
                        if (!engines.Any(e => string.Equals(e.EngineId, id, StringComparison.Ordinal)))
                        {
                            engines.Add(new DockerEngine(distro.Name, id));
                        }
                    }
                    else if (!IsDockerMissing(probe))
                    {
                        errors.Add($"Docker (WSL: {distro.Name}): {probe.ErrorText}");
                    }
                }
            }
        }

        return new Discovery(engines, notes, errors, wslSeen);
    }

    private async Task<List<DevCleanupItem>> MeasureEngineAsync(DockerEngine engine, List<string> errors, CancellationToken cancellationToken)
    {
        ProcessResult df = await ProbeAsync(engine.Distro, Commands.SystemDf, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DockerDiskUsage> usage = [];
        if (df.Succeeded)
        {
            usage = DockerOutputParser.ParseSystemDf(df.StandardOutput);
        }
        else
        {
            errors.Add($"{engine.Label}: docker system df failed: {df.ErrorText}");
        }

        DockerDiskUsage? Row(string type) =>
            usage.FirstOrDefault(u => string.Equals(u.Type, type, StringComparison.OrdinalIgnoreCase));

        DockerDiskUsage? buildRow = Row("Build Cache");
        long? buildCache = buildRow?.ReclaimableBytes;
        if (buildRow is null)
        {
            // A partly corrupt engine can fail "system df" on its container list yet still size its build cache.
            ProcessResult du = await ProbeAsync(engine.Distro, Commands.BuilderDu, cancellationToken).ConfigureAwait(false);
            buildCache = du.Succeeded ? DockerOutputParser.ParseBuilderDuReclaimable(du.StandardOutput) : null;
            if (!du.Succeeded)
            {
                errors.Add($"{engine.Label}: docker builder du failed: {du.ErrorText}");
            }
        }

        var items = new List<DevCleanupItem>();
        bool measured = usage.Count > 0;

        if (buildCache is null or > 0)
        {
            items.Add(Item(engine, "Build cache unused for 7+ days", Commands.PruneBuildCache, buildCache, DevCleanupRisk.Low, selected: true,
                "Intermediate layers from image builds. Only cache entries unused for 7 days or more are pruned; "
                + "the size shown is all reclaimable cache, so the real figure may be lower.") with { SizeIsEstimate = true });
        }

        DockerDiskUsage? images = Row("Images");
        if (!measured || images is { ReclaimableBytes: > 0 })
        {
            items.Add(Item(engine, "Unused images older than 7 days", Commands.PruneImages, images?.ReclaimableBytes, DevCleanupRisk.Medium, selected: false,
                "Images no container uses, created more than 7 days ago. They are pulled or rebuilt again when next needed. "
                + "Size is an upper bound.") with { SizeIsEstimate = true });
        }

        DockerDiskUsage? containers = Row("Containers");
        int stopped = containers is null ? 0 : Math.Max(0, containers.TotalCount - containers.ActiveCount);
        if (!measured || stopped > 0)
        {
            string count = containers is null ? "Stopped containers" : $"{stopped} stopped container(s)";
            items.Add(Item(engine, "Stopped containers", Commands.PruneContainers, containers?.ReclaimableBytes, DevCleanupRisk.Low, selected: true,
                $"{count}; their writable layers and logs are removed. Running containers are not touched."));
        }

        DockerDiskUsage? volumes = Row("Local Volumes");
        if (!measured || volumes is { ReclaimableBytes: > 0 })
        {
            items.Add(Item(engine, "Unused anonymous volumes", Commands.PruneVolumes, volumes?.ReclaimableBytes, DevCleanupRisk.High, selected: false,
                "Volumes not attached to any container. Size is an upper bound.") with
            {
                SizeIsEstimate = true,
                Warning = "Volumes are where containers keep databases and other data. Removed volumes cannot be recovered. "
                    + "Only select this if you are sure nothing in them is needed.",
            });
        }

        return items;
    }

    private DevCleanupItem Item(DockerEngine engine, string title, string[] arguments, long? size, DevCleanupRisk risk, bool selected, string reason) =>
        new(Id, engine.Label, title, reason, size, risk)
        {
            SelectedByDefault = selected,
            Payload = new DockerAction(engine, arguments),
        };

    /// <summary>A measurement command, bounded so a wedged daemon cannot hang the scan.</summary>
    private async Task<ProcessResult> ProbeAsync(string? distro, string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            return await RunDockerAsync(distro, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(1, string.Empty, $"Timed out after {ProbeTimeout.TotalMinutes:0} minutes.");
        }
    }

    private Task<ProcessResult> RunDockerAsync(string? distro, string[] arguments, CancellationToken cancellationToken) =>
        distro is null
            ? _runner.RunAsync("docker", arguments, null, cancellationToken)
            : _runner.RunAsync("wsl", ["-d", distro, "--exec", "docker", .. arguments], null, cancellationToken);

    private static bool IsDockerMissing(ProcessResult result) =>
        !result.Started
        || result.ErrorText.Contains("execvpe(docker) failed", StringComparison.OrdinalIgnoreCase)
        || result.ErrorText.Contains("docker: not found", StringComparison.OrdinalIgnoreCase)
        || result.ErrorText.Contains("command not found", StringComparison.OrdinalIgnoreCase);
}
