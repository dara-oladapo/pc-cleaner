using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop.Views;

public partial class DashboardView : UserControl
{
    private DashboardViewModel? _viewModel;
    private bool _subscribed;

    public DashboardView()
    {
        InitializeComponent();
    }

    // The view model is a singleton and this view is rebuilt on every visit, so the subscription lives only while
    // the view is on screen; otherwise each visit would leave a dead view hanging off the view model's event.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Unsubscribe();
        _viewModel = DataContext as DashboardViewModel;
        if (this.IsAttachedToVisualTree())
        {
            Subscribe();
        }

        UpdateCapacitySegments();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        UpdateCapacitySegments();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_viewModel is not null && !_subscribed)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (_viewModel is not null && _subscribed)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribed = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.OccupiedShare)
            or nameof(DashboardViewModel.ReclaimableShare)
            or nameof(DashboardViewModel.FreeShare))
        {
            UpdateCapacitySegments();
        }
    }

    /// <summary>
    /// The capacity readout's three star columns — what MAUI binds through <c>ShareToStarLengthConverter</c>.
    /// </summary>
    /// <remarks>
    /// <para>Set here rather than bound because an Avalonia <see cref="ColumnDefinition"/> is not a styled element:
    /// it has no DataContext, so <c>{Binding OccupiedShare}</c> on its Width has nothing to resolve against.</para>
    /// <para>The zero-width case is handled explicitly. A segment whose share is 0 is collapsed (an absolute width of
    /// 0 and not visible), not given <c>0*</c>: with every column at <c>0*</c> — a drive that reports a zero total, or
    /// no reading yet — Avalonia's Grid still laid each column out a pixel wide, which drew three hairline slivers on
    /// what should be an empty bar. Now a zero-share segment draws nothing, and with every share at 0 the bar shows
    /// only its empty Sunken track. A non-finite or out-of-range share clamps
    /// into 0..1, as in the MAUI converter, so a bad figure can never draw a segment wider than the drive.</para>
    /// </remarks>
    private void UpdateCapacitySegments()
    {
        SetSegment(0, _viewModel?.OccupiedShare);
        SetSegment(1, _viewModel?.ReclaimableShare);
        SetSegment(2, _viewModel?.FreeShare);
    }

    private void SetSegment(int column, double? share)
    {
        double clamped = share is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
        bool drawn = clamped > 0;
        CapacitySegments.ColumnDefinitions[column].Width =
            drawn ? new GridLength(clamped, GridUnitType.Star) : new GridLength(0, GridUnitType.Pixel);
        CapacitySegments.Children[column].IsVisible = drawn;
    }
}