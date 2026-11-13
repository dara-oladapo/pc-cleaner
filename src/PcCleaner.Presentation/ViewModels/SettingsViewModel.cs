using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>The Settings page, and the one-time suggestion to turn developer mode on.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(IAppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _settings.Changed += (_, _) => OnPropertyChanged(nameof(DeveloperMode));
    }

    /// <summary>Two-way bound to the toggle. Only ever changed by the user.</summary>
    public bool DeveloperMode
    {
        get => _settings.DeveloperMode;
        set
        {
            if (_settings.DeveloperMode != value)
            {
                _settings.DeveloperMode = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Once per install, on a machine with git or docker on disk (looked up, never run), asks whether to turn
    /// developer mode on. Declining is remembered the same as accepting; the setting is never turned on without a yes.
    /// </summary>
    public Task SuggestDeveloperModeOnceAsync() => SuggestDeveloperModeOnceAsync(ToolLocator.IsInstalled);

    public async Task SuggestDeveloperModeOnceAsync(Func<string, bool> isInstalled)
    {
        if (!DeveloperModeAdvisor.ShouldSuggest(_settings, isInstalled))
        {
            return;
        }

        _settings.DeveloperModeSuggestionShown = true;
        bool accepted = await _dialogs.ConfirmAsync(
            title: "Turn on developer mode?",
            message: "Git or Docker is installed. Developer mode adds a Developer page that finds Docker build cache, stale git "
                + "worktrees and old build output. It stays off unless you turn it on, and you can change it any time in Settings.",
            acceptText: "Turn on",
            cancelText: "Not now",
            isDestructive: false,
            details: null);

        if (accepted)
        {
            DeveloperMode = true;
        }
    }
}
