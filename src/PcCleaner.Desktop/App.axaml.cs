using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop;

public partial class App : Application
{
    /// <summary>The container, built once the framework is up. Null only before <see cref="OnFrameworkInitializationCompleted"/>.</summary>
    public IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Build();

        // Before the first window exists, so the app opens in the stored theme instead of flashing the
        // system one and correcting itself — same ordering as the MAUI App constructor.
        Services.GetRequiredService<IThemeService>().ApplyStored();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
