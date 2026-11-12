using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.App.Pages;

public partial class DuplicateFinderPage : ContentPage
{
    public DuplicateFinderPage(DuplicateFinderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
