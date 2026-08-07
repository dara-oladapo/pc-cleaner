using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class DuplicateFinderPage : ContentPage
{
    public DuplicateFinderPage(DuplicateFinderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnRemoveFolderClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: string path } && BindingContext is DuplicateFinderViewModel vm)
        {
            vm.RemoveFolderCommand.Execute(path);
        }
    }
}
