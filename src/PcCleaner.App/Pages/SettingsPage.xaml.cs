using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // A disk plugged in or pulled out while the app was open should be reflected the next time the page shows.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshVolumesCommand.Execute(null);
    }
}
