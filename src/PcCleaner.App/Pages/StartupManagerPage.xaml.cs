using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Pages;

public partial class StartupManagerPage : ContentPage
{
    public StartupManagerPage(StartupManagerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: StartupItemViewModel item } && BindingContext is StartupManagerViewModel vm)
        {
            vm.ToggleCommand.Execute(item);
        }
    }
}
