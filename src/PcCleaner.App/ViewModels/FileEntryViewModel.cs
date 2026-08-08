using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class FileEntryViewModel(FileEntry model) : ObservableObject
{
    public FileEntry Model { get; } = model;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>0..1 against the largest file in the same list — drives the row meter's fill width.</summary>
    [ObservableProperty]
    public partial double ShareOfMax { get; set; }

    public string Path => Model.Path;

    public string SizeText => ByteSizeFormatter.Format(Model.SizeBytes);

    /// <summary>
    /// Shown on the large-file list. "Last changed six months ago" is the strongest signal a user has for
    /// whether a big file is still wanted, so the row carries it next to the path.
    /// </summary>
    public string LastModifiedText => Model.LastModifiedUtc.ToLocalTime().ToString("d MMM yyyy");
}
