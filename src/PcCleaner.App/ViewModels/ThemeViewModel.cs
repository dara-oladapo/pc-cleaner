using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcCleaner.App.Services;

namespace PcCleaner.App.ViewModels;

/// <summary>Backs the three-way theme switcher in the navigation rail footer.</summary>
public sealed partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService _themeService;

    public ThemeViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        Selected = themeService.Current;
    }

    [ObservableProperty]
    public partial ThemePreference Selected { get; set; }

    // Three booleans rather than comparing an enum in XAML: MAUI's DataTrigger matches a literal value,
    // and these keep the segmented control's selected state to one binding per segment.
    public bool IsSystem => Selected == ThemePreference.System;

    public bool IsLight => Selected == ThemePreference.Light;

    public bool IsDark => Selected == ThemePreference.Dark;

    [RelayCommand]
    private void Choose(ThemePreference preference)
    {
        _themeService.Set(preference);
        Selected = preference;
    }

    partial void OnSelectedChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsLight));
        OnPropertyChanged(nameof(IsDark));
    }
}
