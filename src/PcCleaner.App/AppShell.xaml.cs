using PcCleaner.App.ViewModels;

namespace PcCleaner.App;

public partial class AppShell : Shell
{
	public AppShell(ThemeViewModel themeViewModel)
	{
		InitializeComponent();

		// Scoped to the footer rather than set on the Shell: the flyout item template binds against each
		// shell item, so giving the Shell itself a view model would put the wrong context under those rows.
		FlyoutFooterRoot.BindingContext = themeViewModel;
	}
}
