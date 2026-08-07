namespace PcCleaner.Core.Models;

public sealed record StartupItem(
    string Id,
    string Name,
    string CommandOrPath,
    StartupItemSource Source,
    StartupItemScope Scope,
    bool IsEnabled);
