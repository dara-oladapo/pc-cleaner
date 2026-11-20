using CommunityToolkit.Mvvm.ComponentModel;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>
/// One tickable line under a tool on the dashboard: a junk category, a duplicate group, a large file, a
/// cloud folder, a developer cleanup action.
/// </summary>
/// <remarks>
/// It holds no selection of its own. The tick is read from and written to the tool's view model, so the
/// dashboard and the tool's page always show the same thing.
/// </remarks>
public sealed partial class DashboardPartViewModel : ObservableObject
{
    private readonly Func<bool> _isSelected;
    private readonly Action<bool> _setSelected;
    private readonly Func<string> _sizeText;
    private bool _refreshing;

    public DashboardPartViewModel(
        string title,
        string detail,
        Func<bool> isSelected,
        Action<bool> setSelected,
        Func<string> sizeText,
        bool canSelect = true)
    {
        Title = title;
        Detail = detail;
        CanSelect = canSelect;
        _isSelected = isSelected;
        _setSelected = setSelected;
        _sizeText = sizeText;
        Refresh();
    }

    public string Title { get; }

    /// <summary>A second line: a path, a count, a reason. Empty when there is nothing useful to add.</summary>
    public string Detail { get; }

    public bool HasDetail => Detail.Length > 0;

    /// <summary>False for something listed for information that can never be ticked (a read-only cloud folder).</summary>
    public bool CanSelect { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string SizeText { get; set; } = string.Empty;

    /// <summary>Re-reads the tick and the size from the tool, without writing anything back.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            IsSelected = _isSelected();
            SizeText = _sizeText();
        }
        finally
        {
            _refreshing = false;
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_refreshing)
        {
            return;
        }

        _setSelected(value);

        // The tool has the last word: a folder that cannot be freed stays unticked whatever was clicked.
        Refresh();
    }
}
