using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Dispatch.UI.Controls;

/// <summary>
/// Renders one glyph from the icon set at the theme's icon size and stroke
/// weight.
/// </summary>
/// <remarks>
/// The stroke is bound to <see cref="TemplatedControl.Foreground"/> in the
/// control theme, which is how an icon inherits colour from its context instead
/// of the set shipping a copy per colour. Setting Foreground — or letting it
/// inherit from a parent — recolours the glyph.
/// </remarks>
public sealed class Icon : TemplatedControl
{
    /// <summary>Defines the <see cref="Data"/> property.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    /// <summary>Defines the <see cref="Size"/> property.</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 20d);

    /// <summary>The glyph to draw, taken from <c>Assets/Icons.axaml</c>.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Edge length of the square the glyph is drawn in. The set is drawn on a
    /// 20px grid; other sizes scale it uniformly.
    /// </summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }
}
