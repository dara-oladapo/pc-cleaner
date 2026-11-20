using PcCleaner.Presentation.Services;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App;

public partial class AppShell : Shell
{
	private readonly DeveloperModeSettings _developerMode;
	private readonly SettingsViewModel _settingsViewModel;
	private bool _suggestionChecked;

	public AppShell(ThemeViewModel themeViewModel, DeveloperModeSettings developerMode, SettingsViewModel settingsViewModel)
	{
		InitializeComponent();
		_developerMode = developerMode;
		_settingsViewModel = settingsViewModel;

		// Scoped to the footer rather than set on the Shell: the flyout item template binds against each
		// shell item, so giving the Shell itself a view model would put the wrong context under those rows.
		FlyoutFooterRoot.BindingContext = themeViewModel;

		// The Developer page exists only while developer mode is on.
		DeveloperItem.IsVisible = developerMode.IsEnabled;
		developerMode.Changed += (_, _) =>
			MainThread.BeginInvokeOnMainThread(() => DeveloperItem.IsVisible = _developerMode.IsEnabled);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		// Once per launch at most; the view model makes it once per install.
		if (_suggestionChecked)
		{
			return;
		}

		_suggestionChecked = true;
		try
		{
			await _settingsViewModel.SuggestDeveloperModeOnceAsync();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			// A suggestion is never worth crashing over.
		}
	}
}
