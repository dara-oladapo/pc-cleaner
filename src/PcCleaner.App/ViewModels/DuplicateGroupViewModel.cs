using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroup Model { get; }

    public ObservableCollection<FileEntryViewModel> Files { get; }

    public DuplicateGroupViewModel(DuplicateGroup model)
    {
        Model = model;

        // Keep the first copy unselected by default; pre-select the rest as the ones to remove.
        var fileVms = model.Files
            .Select((f, index) => new FileEntryViewModel(f) { IsSelected = index > 0 })
            .ToList();

        foreach (var fileVm in fileVms)
        {
            fileVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileEntryViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(ReclaimableBytesSelected));
                }
            };
        }

        Files = new ObservableCollection<FileEntryViewModel>(fileVms);
    }

    public string SizeEachText => ByteSizeFormatter.Format(Model.SizeBytesEach);

    public long ReclaimableBytesSelected => Files.Where(f => f.IsSelected).Sum(f => f.Model.SizeBytes);

    /// <summary>0..1 share of the largest group's reclaimable size — drives this group header's inline meter bar.</summary>
    [ObservableProperty]
    public partial double ShareOfMax { get; set; } = 1;

    public void RemoveFiles(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            var match = Files.FirstOrDefault(f => f.Path == path);
            if (match is not null)
            {
                Files.Remove(match);
            }
        }

        OnPropertyChanged(nameof(ReclaimableBytesSelected));
    }
}
