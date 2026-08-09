using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class StartupManagerPage : ContentPage
{
    public StartupManagerPage(StartupManagerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
