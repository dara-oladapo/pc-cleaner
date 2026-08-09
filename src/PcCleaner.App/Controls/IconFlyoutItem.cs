using Microsoft.Maui.Controls.Shapes;

namespace PcCleaner.App.Controls;

/// <summary>
/// A <see cref="FlyoutItem"/> that carries vector path geometry for its rail icon.
/// </summary>
/// <remarks>
/// Shell binds its <c>ItemTemplate</c> against the shell item itself, and <see cref="BaseShellItem"/> only
/// offers <c>Icon</c> as an <c>ImageSource</c> — a baked bitmap that can't follow the theme or the
/// selected state. Adding the geometry here lets the template bind a real <c>Path.Data</c> and tint it,
/// so one icon definition covers light, dark, selected and unselected.
/// </remarks>
public sealed class IconFlyoutItem : FlyoutItem
{
    public static readonly BindableProperty IconDataProperty =
        BindableProperty.Create(nameof(IconData), typeof(Geometry), typeof(IconFlyoutItem));

    public Geometry? IconData
    {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
}
