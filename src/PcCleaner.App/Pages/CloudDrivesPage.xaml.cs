using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App.Pages;

public partial class CloudDrivesPage : ContentPage
{
    public CloudDrivesPage(CloudDrivesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
