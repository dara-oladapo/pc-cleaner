using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class LargeFilesPage : ContentPage
{
    public LargeFilesPage(LargeFilesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
