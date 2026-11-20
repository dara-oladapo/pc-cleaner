using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>One of a cloud folder's largest downloaded files, in the expanded list under the folder.</summary>
public sealed partial class CloudFileViewModel : ObservableObject
{
    public CloudFileViewModel(CloudFileVerdict verdict, string rootPath, bool rootCanFreeUp)
    {
        Verdict = verdict;
        CanToggle = verdict.IsEligible && rootCanFreeUp;
        IsSelected = CanToggle;

        string relative = Path.GetRelativePath(rootPath, verdict.File.Path);
        Name = Path.GetFileName(relative);
        Folder = Path.GetDirectoryName(relative) is { Length: > 0 } dir ? dir : "(top level)";
    }

    public CloudFileVerdict Verdict { get; }

    public CloudFileState File => Verdict.File;

    public string Name { get; }

    public string Folder { get; }

    public string SizeText => ByteSizeFormatter.Format(File.AllocatedBytes);

    public bool IsEligible => Verdict.IsEligible;

    /// <summary>False for files that stay on the device whatever the user ticks. Their box is shown disabled.</summary>
    public bool CanToggle { get; }

    public string ReasonText => CloudEligibilityRules.Describe(Verdict.Reason);

    /// <summary>Starts ticked for eligible files only. Unticking keeps that one file on the device.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        // A file that is not eligible can never be selected, whatever a binding tries to do.
        if (value && !CanToggle)
        {
            IsSelected = false;
        }
    }
}
