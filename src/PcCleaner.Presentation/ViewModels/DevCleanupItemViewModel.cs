using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Presentation.ViewModels;

/// <summary>One row on the Developer page.</summary>
public sealed partial class DevCleanupItemViewModel(DevCleanupItem model) : ObservableObject
{
    public DevCleanupItem Model { get; } = model;

    /// <summary>Only Docker build cache and stopped containers start ticked; the provider decides.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = model.SelectedByDefault;

    [ObservableProperty]
    public partial double ShareOfMax { get; set; } = 1;

    public string Title => Model.Title;

    public string Reason => Model.Reason;

    public string Path => Model.Path ?? string.Empty;

    public bool HasPath => Model.Path is not null;

    public string? Warning => Model.Warning;

    public bool HasWarning => Model.Warning is not null;

    public long Bytes => Model.SizeBytes ?? 0;

    public string SizeText => Model.SizeBytes is not { } bytes
        ? "size unknown"
        : Model.SizeIsEstimate ? $"up to {ByteSizeFormatter.Format(bytes)}" : ByteSizeFormatter.Format(bytes);

    public string RiskText => Model.Risk switch
    {
        DevCleanupRisk.Low => "Low risk",
        DevCleanupRisk.Medium => "Medium risk",
        _ => "High risk",
    };

    public bool IsHighRisk => Model.Risk == DevCleanupRisk.High;

    public bool IsNotHighRisk => !IsHighRisk;
}

/// <summary>Rows sharing a heading: one Docker engine, one repository's worktrees, one kind of build output.</summary>
public sealed class DevCleanupGroup(string name) : ObservableCollection<DevCleanupItemViewModel>
{
    public string Name { get; } = name;

    public string TotalText => ByteSizeFormatter.Format(this.Sum(i => i.Bytes));

    public void NotifyTotalChanged() => OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalText)));
}
