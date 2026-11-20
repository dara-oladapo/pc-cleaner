using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Presentation.ViewModels;

/// <param name="model">The file found by a scan.</param>
/// <param name="cloudRoot">The cloud drive folder the file lives in, or null for an ordinary file.</param>
public sealed partial class FileEntryViewModel(FileEntry model, CloudSyncRoot? cloudRoot = null) : ObservableObject
{
    public FileEntry Model { get; } = model;

    /// <summary>The cloud drive folder this file is in. Files in one are "cloud only": never deleted from here.</summary>
    public CloudSyncRoot? CloudRoot { get; } = cloudRoot;

    public bool IsCloudOnly => CloudRoot is not null;

    /// <summary>False for a cloud-only file: it can be listed, never ticked.</summary>
    public bool CanSelect => CloudRoot is null;

    public string CloudNote => CloudRoot is null
        ? string.Empty
        : $"In {CloudRoot.DisplayName}. Never deleted here. To free the space, use Cloud Drives.";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    // The rule lives here, not in the checkbox, so no screen and no "select all" can tick a cloud-only file.
    partial void OnIsSelectedChanged(bool value)
    {
        if (value && IsCloudOnly)
        {
            IsSelected = false;
        }
    }

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
