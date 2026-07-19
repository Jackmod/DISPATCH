using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Controls;

/// <summary>
/// A raised surface with an optional eyebrow and title.
/// </summary>
/// <remarks>
/// The accent rule under the title is a three-lozenge lightbar rather than a
/// plain divider. It is the motif that ties the intro, the install progress
/// rail and every section heading in the app to the same idea, which is what
/// stops a dark theme from reading as a generic dark theme.
/// </remarks>
public sealed class Card : ContentControl
{
    /// <summary>Defines the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Card, string?>(nameof(Title));

    /// <summary>Defines the <see cref="Eyebrow"/> property.</summary>
    public static readonly StyledProperty<string?> EyebrowProperty =
        AvaloniaProperty.Register<Card, string?>(nameof(Eyebrow));

    /// <summary>Defines the <see cref="IsInteractive"/> property.</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<Card, bool>(nameof(IsInteractive));

    /// <summary>Card title, set in the h2 style. Omit for an untitled surface.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Uppercase micro label above the title. Omit if there is nothing to categorise.</summary>
    public string? Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    /// <summary>
    /// Whether the card responds to hover. Only set this when the whole card is
    /// genuinely clickable; a card that lifts but does nothing is a lie.
    /// </summary>
    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Card);
}
