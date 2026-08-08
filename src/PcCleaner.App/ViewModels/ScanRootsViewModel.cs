using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;

namespace PcCleaner.App.ViewModels;

/// <summary>
/// The "which folders do we scan" half of any tool that walks user folders — the duplicate finder and the
/// large file finder need exactly the same list, defaults, and add/remove behaviour.
/// </summary>
public abstract partial class ScanRootsViewModel : ObservableObject
{
    private readonly IFolderPickerService _folderPicker;

    public ObservableCollection<string> RootPaths { get; } = [];

    [ObservableProperty]
    public partial string NewFolderPath { get; set; } = string.Empty;

    protected ScanRootsViewModel(IFolderPickerService folderPicker)
    {
        _folderPicker = folderPicker;

        foreach (string folder in DefaultFolders())
        {
            RootPaths.Add(folder);
        }
    }

    /// <summary>Set by the derived tool so a rejected folder can explain itself in that tool's status line.</summary>
    protected abstract void ReportRootPathProblem(string message);

    private static IEnumerable<string> DefaultFolders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
        ];

        return candidates.Where(Directory.Exists).Distinct();
    }

    /// <summary>Opens the OS folder browser. The typed entry below it still works if the picker is unavailable.</summary>
    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        string? picked = await _folderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            AddPath(picked);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        string path = NewFolderPath.Trim();
        if (path.Length == 0)
        {
            return;
        }

        if (AddPath(path))
        {
            NewFolderPath = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveFolder(string path) => RootPaths.Remove(path);

    private bool AddPath(string path)
    {
        if (!Directory.Exists(path))
        {
            ReportRootPathProblem($"'{path}' isn't a folder that exists.");
            return false;
        }

        if (!RootPaths.Contains(path))
        {
            RootPaths.Add(path);
        }

        return true;
    }
}
