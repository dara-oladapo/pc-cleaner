using Microsoft.Extensions.DependencyInjection;
using PcCleaner.App.Services;

namespace PcCleaner.App;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services, IThemeService themeService)
	{
		_services = services;

		InitializeComponent();

		// Before the first window exists, so the app opens in the theme the user chose last time instead
		// of flashing the system theme and correcting itself.
		themeService.ApplyStored();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_services.GetRequiredService<AppShell>())
		{
			Width = 1120,
			Height = 780,
			// Floor the window size so the 216px navigation rail, the four-across dashboard cards, and the
			// row layout (checkbox / content / meter / size) all still fit. Below this the app is still
			// usable, just no smaller.
			MinimumWidth = 900,
			MinimumHeight = 600,
		};
	}
}
