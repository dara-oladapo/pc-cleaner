using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App.Pages;

public partial class DeveloperCleanupPage : ContentPage
{
    public DeveloperCleanupPage(DeveloperCleanupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
