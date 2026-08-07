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
			Width = 980,
			Height = 760,
			// Floor the window size so the three-column row layout (checkbox / content / size) and the
			// hero stat never get crushed — below this the app is still usable, just no smaller.
			MinimumWidth = 720,
			MinimumHeight = 520,
		};
	}
}