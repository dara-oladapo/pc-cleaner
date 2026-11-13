namespace PcCleaner.Core.Abstractions;

/// <summary>User settings that persist across launches. The app backs this with the platform's preferences store.</summary>
public interface IAppSettings
{
    /// <summary>
    /// Off by default and never turned on automatically. While off, the Developer page is hidden, no Docker,
    /// WSL or git command is run, and junk sources marked as developer-only are left out of the junk scan.
    /// </summary>
    bool DeveloperMode { get; set; }

    /// <summary>Set once the one-time "turn on developer mode?" suggestion has been shown, whatever the answer.</summary>
    bool DeveloperModeSuggestionShown { get; set; }

    /// <summary>Raised after any setting changes.</summary>
    event EventHandler? Changed;
}

/// <summary>An in-memory <see cref="IAppSettings"/>: the default for tests and for hosts without a preferences store.</summary>
public sealed class InMemoryAppSettings : IAppSettings
{
    private bool _developerMode;
    private bool _suggestionShown;

    public event EventHandler? Changed;

    public bool DeveloperMode
    {
        get => _developerMode;
        set
        {
            if (_developerMode != value)
            {
                _developerMode = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool DeveloperModeSuggestionShown
    {
        get => _suggestionShown;
        set
        {
            if (_suggestionShown != value)
            {
                _suggestionShown = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
