using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class FileEntryViewModel(FileEntry model) : ObservableObject
{
    public FileEntry Model { get; } = model;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Path => Model.Path;

    public string SizeText => ByteSizeFormatter.Format(Model.SizeBytes);
}
