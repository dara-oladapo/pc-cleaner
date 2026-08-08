using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class DuplicateFinderPage : ContentPage
{
    public DuplicateFinderPage(DuplicateFinderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
