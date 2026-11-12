using Avalonia.Controls;
using PcCleaner.Desktop.ViewModels;

namespace PcCleaner.Desktop;

public partial class MainWindow : Window
{
    // Parameterless constructor for the XAML loader and the designer only.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ShellViewModel shell)
        : this()
    {
        DataContext = shell;
    }
}
