using Microsoft.Extensions.DependencyInjection;
using PcCleaner.App.ViewModels;

namespace PcCleaner.App.Controls;

/// <summary>
/// The app's confirmation dialog, overlaid on a page's content. One per page; all of them share the
/// single <see cref="DialogHostViewModel"/>, so whichever page is on screen shows the request.
/// </summary>
public partial class DialogHost : ContentView
{
    public DialogHost()
    {
        InitializeComponent();

        // Resolved from the container rather than injected: a ContentView placed in XAML is constructed
        // by the XAML loader, which doesn't run constructor injection. The host is inert without it, so
        // a null container simply means no dialog rather than a crash on startup.
        BindingContext = Application.Current?.Handler?.MauiContext?.Services?.GetService<DialogHostViewModel>();
    }
}
