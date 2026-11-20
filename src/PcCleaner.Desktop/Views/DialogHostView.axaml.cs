using Avalonia.Controls;
using Avalonia.Input;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop.Views;

public partial class DialogHostView : UserControl
{
    public DialogHostView()
    {
        InitializeComponent();
    }

    // Dismissing without choosing is a no, never a yes (DESIGN.md, "Destructive actions").
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DialogHostViewModel dialog)
        {
            dialog.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
