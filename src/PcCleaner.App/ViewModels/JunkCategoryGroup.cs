using System.Collections.ObjectModel;
using System.ComponentModel;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.ViewModels;

/// <summary>
/// One category's worth of junk results, for the grouped list on the junk screen.
/// </summary>
/// <remarks>
/// The grouping is real: it comes from <c>JunkItem.Category</c> on the model, not from a wish for section
/// headings. Twenty ungrouped rows of "cache" and "temp" read as one undifferentiated wall; grouped, you
/// can decide about browser caches without reading every row.
/// </remarks>
public sealed class JunkCategoryGroup(string name) : ObservableCollection<JunkItemViewModel>
{
    public string Name { get; } = name;

    public string TotalText => ByteSizeFormatter.Format(this.Sum(i => i.Model.SizeBytes));

    /// <summary>Called after items land, since the header total is derived from the collection's contents.</summary>
    public void NotifyTotalChanged() => OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalText)));
}
