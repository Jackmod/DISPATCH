using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace Dispatch.UI.Intro;

/// <summary>
/// The launch animation. Raises <see cref="Completed"/> when it has finished,
/// been skipped, or been cut short by reduced motion.
/// </summary>
/// <remarks>
/// Driven from code rather than from styles because it is one precisely-timed
/// sequence rather than a set of state responses. Expressing it as styles would
/// scatter four overlapping movements across a dozen selectors and make the
/// timing impossible to read in one place.
/// </remarks>
public partial class IntroView : UserControl
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _finished;

    /// <summary>Constructs the intro.</summary>
    public IntroView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        PointerPressed += (_, _) => Skip();
        KeyDown += (_, _) => Skip();
    }

    /// <summary>Raised once the intro is done and the shell should take over.</summary>
    public event EventHandler? Completed;

    private bool ReducedMotion =>
        this.TryFindResource("MotionEnabled", ThemeVariant.Dark, out var enabled) && enabled is false;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Focus();

        // Reduced motion cuts straight to the app rather than playing a
        // shortened version of the same thing.
        if (ReducedMotion)
        {
            Finish();
            return;
        }

        try
        {
            await PlayAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Skipped. Finish() has already run or is about to.
        }

        Finish();
    }

    private async Task PlayAsync(CancellationToken token)
    {
        var width = Bounds.Width > 0 ? Bounds.Width : 1240;

        // The glow comes up under everything so the field is never flat black.
        var glow = Animate(GlowFadeIn(), Glow, token);

        // 0-560ms: the scan line crosses.
        var scan = Animate(ScanSweep(width), Scan, token);
        var scanOpacity = Animate(ScanOpacity(), Scan, token);

        // 120-980ms: letters draw on, 60ms apart.
        var letters = DrawLettersAsync(token);

        await Task.WhenAll(glow, scan, scanOpacity, letters);
        token.ThrowIfCancellationRequested();

        // 900-1180ms: the lightbar pulses once.
        await PulseLightbarAsync(token);
        token.ThrowIfCancellationRequested();

        // 1150-1400ms: lift and fade into the app.
        await Task.WhenAll(
            Animate(Lift(), Composition, token),
            Animate(FadeOut(), Root, token));
    }

    private async Task DrawLettersAsync(CancellationToken token)
    {
        // Fully qualified: ImplicitUsings brings in System.IO, whose Path
        // collides with the shape.
        Avalonia.Controls.Shapes.Path[] letters = [L0, L1, L2, L3, L4, L5, L6, L7];

        var draws = letters.Select((letter, index) =>
            Animate(LetterDraw(TimeSpan.FromMilliseconds(120 + (index * 60))), letter, token));

        await Task.WhenAll(draws);

        // The skip hint only appears once the wordmark is legible; offering to
        // skip something the viewer has not yet seen is just noise.
        await Animate(HintFadeIn(), SkipHint, token);
    }

    private async Task PulseLightbarAsync(CancellationToken token)
    {
        Border[] segments = [S0, S1, S2, S3, S4];

        // Outward from the centre, so the gold segment leads.
        int[] order = [2, 1, 3, 0, 4];

        var pulses = order.Select((segmentIndex, position) =>
            Animate(SegmentPulse(TimeSpan.FromMilliseconds(position * 45)), segments[segmentIndex], token));

        await Task.WhenAll(pulses);
    }

    /// <remarks>
    /// RunAsync takes a Visual, not an Animatable, so a TranslateTransform
    /// cannot be animated directly. Transform sub-properties are set on the
    /// control instead — the same mechanism Reveal.axaml uses in XAML — and
    /// Avalonia builds the transform behind the scenes.
    /// </remarks>
    private static Task Animate(Animation animation, Visual target, CancellationToken token) =>
        animation.RunAsync(target, token);

    // ===== The sequence, as data =========================================

    private static Animation GlowFadeIn() => new()
    {
        Duration = TimeSpan.FromMilliseconds(900),
        Easing = new SineEaseOut(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (OpacityProperty, 0d)),
            Frame(1d, (OpacityProperty, 0.55d)),
        },
    };

    private static Animation ScanSweep(double width) => new()
    {
        Duration = TimeSpan.FromMilliseconds(560),
        Easing = new CubicEaseInOut(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (TranslateTransform.XProperty, 0d)),
            Frame(1d, (TranslateTransform.XProperty, width)),
        },
    };

    private static Animation ScanOpacity() => new()
    {
        Duration = TimeSpan.FromMilliseconds(560),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (OpacityProperty, 0d)),
            Frame(0.12d, (OpacityProperty, 0.9d)),
            Frame(0.82d, (OpacityProperty, 0.9d)),
            Frame(1d, (OpacityProperty, 0d)),
        },
    };

    private static Animation LetterDraw(TimeSpan delay) => new()
    {
        Duration = TimeSpan.FromMilliseconds(420),
        Delay = delay,
        Easing = new CubicEaseOut(),
        FillMode = FillMode.Both,
        Children =
        {
            Frame(0d, (Shape.StrokeDashOffsetProperty, 120d)),
            Frame(1d, (Shape.StrokeDashOffsetProperty, 0d)),
        },
    };

    private static Animation SegmentPulse(TimeSpan delay) => new()
    {
        Duration = TimeSpan.FromMilliseconds(280),
        Delay = delay,
        Easing = new CubicEaseOut(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (OpacityProperty, 0.12d)),
            Frame(0.45d, (OpacityProperty, 1d)),
            Frame(1d, (OpacityProperty, 0.6d)),
        },
    };

    private static Animation HintFadeIn() => new()
    {
        Duration = TimeSpan.FromMilliseconds(200),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (OpacityProperty, 0d)),
            Frame(1d, (OpacityProperty, 1d)),
        },
    };

    private static Animation Lift() => new()
    {
        Duration = TimeSpan.FromMilliseconds(250),
        Easing = new CubicEaseIn(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (TranslateTransform.YProperty, 0d)),
            Frame(1d, (TranslateTransform.YProperty, -8d)),
        },
    };

    private static Animation FadeOut() => new()
    {
        Duration = TimeSpan.FromMilliseconds(250),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (OpacityProperty, 1d)),
            Frame(1d, (OpacityProperty, 0d)),
        },
    };

    private static KeyFrame Frame(double cue, params (AvaloniaProperty Property, object Value)[] setters)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };

        foreach (var (property, value) in setters)
        {
            frame.Setters.Add(new Setter(property, value));
        }

        return frame;
    }

    // ===== Skip and completion ===========================================

    private void Skip()
    {
        if (_finished)
        {
            return;
        }

        _cancellation.Cancel();
        Finish();
    }

    private void Finish()
    {
        // Both the natural end and a skip land here, and a skip during the
        // final fade can reach it twice.
        if (_finished)
        {
            return;
        }

        _finished = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
