using Microsoft.Extensions.DependencyInjection;

namespace PcCleaner.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell())
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