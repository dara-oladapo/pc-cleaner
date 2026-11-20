using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Free space and connected volumes change while the app is open, and a scan or a clean done on a tool's
    // own page changes what the tool rows list; re-read both whenever the page shows.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshVolumes();
        _viewModel.RebuildTools();
    }
}
