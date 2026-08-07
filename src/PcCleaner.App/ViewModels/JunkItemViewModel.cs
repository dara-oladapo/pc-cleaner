using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class JunkItemViewModel(JunkItem model) : ObservableObject
{
    public JunkItem Model { get; } = model;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    /// <summary>0..1 share of the largest item currently in the list — drives the row's inline meter bar.</summary>
    [ObservableProperty]
    public partial double ShareOfMax { get; set; } = 1;

    public string Description => Model.Description;

    public string Path => Model.Path;

    public string SizeText => ByteSizeFormatter.Format(Model.SizeBytes);

    public string CategoryText => Model.Category switch
    {
        JunkCategory.TempFiles => "Temp Files",
        JunkCategory.BrowserCache => "Browser Cache",
        JunkCategory.SystemCache => "System Cache",
        JunkCategory.SystemLogs => "System Logs",
        JunkCategory.PackageManagerCache => "Package Cache",
        JunkCategory.EmptyFolder => "Empty Folder",
        JunkCategory.TrashBin => "Trash",
        _ => Model.Category.ToString(),
    };
}
