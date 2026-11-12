namespace PcCleaner.Presentation.Services;

/// <summary>Moves the app to one of its top-level tools.</summary>
/// <remarks>
/// An interface rather than a call to <c>Shell.Current</c> so the view models carry no UI framework
/// reference. The MAUI app implements it over Shell; a Linux front end implements it over its own shell.
/// </remarks>
public interface INavigationService
{
    /// <summary>Shows the tool registered under <paramref name="route"/>, e.g. <c>junk</c> or <c>duplicates</c>.</summary>
    Task GoToToolAsync(string route);
}
