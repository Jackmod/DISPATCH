using Avalonia;
using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Controls;

/// <summary>The meaning a <see cref="StatusPill"/> carries.</summary>
public enum StatusTone
{
    /// <summary>Informational, no judgement. The default.</summary>
    Neutral,

    /// <summary>Verified, installed, matching. </summary>
    Good,

    /// <summary>Works, but something wants attention eventually.</summary>
    Warning,

    /// <summary>Broken, conflicting, or mismatched.</summary>
    Bad,

    /// <summary>In progress.</summary>
    Active,
}

/// <summary>
/// A small state label: component versions, mod status, conflict counts.
/// </summary>
/// <remarks>
/// Tone drives colour rather than the caller passing a brush, so that "good"
/// looks identical everywhere it appears and a screen cannot invent its own
/// meaning for green.
/// </remarks>
public sealed class StatusPill : TemplatedControl
{
    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusPill, string?>(nameof(Text));

    /// <summary>Defines the <see cref="Tone"/> property.</summary>
    public static readonly StyledProperty<StatusTone> ToneProperty =
        AvaloniaProperty.Register<StatusPill, StatusTone>(nameof(Tone));

    /// <summary>Defines the <see cref="ShowDot"/> property.</summary>
    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<StatusPill, bool>(nameof(ShowDot), true);

    /// <summary>The label. Kept to one or two words.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>What the state means.</summary>
    public StatusTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>
    /// Whether to draw the leading dot. Colour alone never carries the meaning
    /// — the label always says it too — but the dot makes a row of pills
    /// scannable at a glance.
    /// </summary>
    public bool ShowDot
    {
        get => GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ToneProperty)
        {
            UpdateToneClasses();
        }
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateToneClasses();
    }

    // Tone is projected onto style classes so the control theme can express
    // every appearance in XAML rather than the control choosing brushes.
    private void UpdateToneClasses()
    {
        Classes.Set("neutral", Tone == StatusTone.Neutral);
        Classes.Set("good", Tone == StatusTone.Good);
        Classes.Set("warning", Tone == StatusTone.Warning);
        Classes.Set("bad", Tone == StatusTone.Bad);
        Classes.Set("active", Tone == StatusTone.Active);
    }
}
