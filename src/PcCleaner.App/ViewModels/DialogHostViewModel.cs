using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PcCleaner.App.ViewModels;

/// <summary>
/// State for the in-app confirmation dialog. One instance, shared by every page's dialog host.
/// </summary>
/// <remarks>
/// The app draws its own dialogs rather than calling <c>DisplayAlert</c>. A platform alert is the OS's
/// UI, not ours — it ignores the design system, can't show the manifest of what is about to be deleted,
/// and looks like a system warning rather than part of the app. It also renders differently on Windows
/// and Mac Catalyst, so the most consequential moment in the app would be the least consistent one.
/// </remarks>
public sealed partial class DialogHostViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _completion;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AcceptText { get; set; } = "OK";

    [ObservableProperty]
    public partial string CancelText { get; set; } = "Cancel";

    /// <summary>Paints the affirmative button with Danger. Reserved for actions that remove data.</summary>
    [ObservableProperty]
    public partial bool IsDestructive { get; set; }

    [ObservableProperty]
    public partial bool HasDetails { get; set; }

    /// <summary>A few lines naming what is actually about to go — "982 MB  Chrome cache".</summary>
    public ObservableCollection<string> Details { get; } = [];

    public Task<bool> ShowAsync(
        string title,
        string message,
        string acceptText,
        string cancelText,
        bool isDestructive,
        IReadOnlyList<string>? details)
    {
        // A second request while one is open would otherwise strand the first caller's await forever.
        // Nothing in the app does this today, but "the dialog is already up" must never mean "deleted".
        _completion?.TrySetResult(false);

        Title = title;
        Message = message;
        AcceptText = acceptText;
        CancelText = cancelText;
        IsDestructive = isDestructive;

        Details.Clear();
        foreach (string line in details ?? [])
        {
            Details.Add(line);
        }

        HasDetails = Details.Count > 0;

        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsOpen = true;
        return _completion.Task;
    }

    [RelayCommand]
    private void Accept() => Close(true);

    /// <summary>Also the scrim tap. Dismissing without choosing is a no, never a yes.</summary>
    [RelayCommand]
    private void Cancel() => Close(false);

    private void Close(bool result)
    {
        IsOpen = false;
        var completion = _completion;
        _completion = null;
        completion?.TrySetResult(result);
    }
}
