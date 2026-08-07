using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class JunkCleanupPage : ContentPage
{
    public JunkCleanupPage(JunkCleanupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
