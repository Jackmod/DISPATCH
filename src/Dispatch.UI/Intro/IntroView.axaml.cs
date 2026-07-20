using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Dispatch.Core.Audio;

namespace Dispatch.UI.Intro;

/// <summary>
/// The launch animation. Runs for the full length of the launch siren, cannot be
/// skipped, and raises <see cref="Completed"/> when it finishes.
/// </summary>
/// <remarks>
/// Driven from code rather than from styles because it is one precisely-timed
/// sequence rather than a set of state responses. Expressing it as styles would
/// scatter four overlapping movements across a dozen selectors and make the
/// timing impossible to read in one place.
///
/// <para>
/// The intro is deliberately unskippable and lasts exactly as long as the siren:
/// the wordmark draws on, the lightbar pulses, then the finished mark holds under
/// the patrol lights until the siren fades, and the two end together.
/// </para>
/// </remarks>
public partial class IntroView : UserControl
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _finished;

    // The siren bytes, loaded once, and the length the whole intro is timed to.
    private byte[]? _sirenBytes;
    private TimeSpan _introLength = FallbackLength;

    // Used only if the siren asset is missing, so the intro still feels whole.
    private static readonly TimeSpan FallbackLength = TimeSpan.FromSeconds(3.5);

    // The siren plays at half its recorded amplitude.
    private const double SirenVolume = 0.5;

    /// <summary>Constructs the intro.</summary>
    public IntroView()
    {
        InitializeComponent();

        Loaded += OnLoaded;

        // Cancel in-flight animations if the window closes mid-intro, so nothing
        // runs on a torn-down control. This is not a user skip — there is none.
        Unloaded += (_, _) => _cancellation.Cancel();
    }

    /// <summary>Raised once the intro is done and the shell should take over.</summary>
    public event EventHandler? Completed;

    /// <summary>
    /// The sound player used for the launch cue.
    /// </summary>
    /// <remarks>
    /// A settable property rather than a constructor parameter because the intro
    /// is instantiated from XAML with no injection point. The composition root
    /// assigns the real player before the window shows; left null, the intro is
    /// simply silent.
    /// </remarks>
    public static ISoundPlayer? SoundPlayer { get; set; }

    private static readonly Uri SirenAsset = new("avares://Dispatch.UI/Assets/Sounds/intro-siren.wav");

    private bool ReducedMotion =>
        this.TryFindResource("MotionEnabled", ThemeVariant.Dark, out var enabled) && enabled is false;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The siren asset is what the whole intro is timed to, so it is read
        // before anything animates, whether or not it can actually be played.
        LoadSiren();

        // Reduced motion cuts straight to the app rather than holding a static
        // frame for several seconds — that is an accessibility setting, not a
        // user skip, and forcing motion-sensitive users to sit through it would
        // be the wrong call.
        if (ReducedMotion)
        {
            Finish();
            return;
        }

        PlaySiren();

        try
        {
            await PlayAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The window closed mid-intro. Finish() is a no-op after teardown.
        }

        Finish();
    }

    /// <summary>
    /// Reads the siren asset once and times the intro to it. A missing or
    /// unreadable asset must never fail the launch, so the intro keeps its
    /// fallback length and simply plays no sound.
    /// </summary>
    private void LoadSiren()
    {
        try
        {
            using var stream = AssetLoader.Open(SirenAsset);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _sirenBytes = memory.ToArray();

            var pcm = WavAudio.Read(_sirenBytes);
            if (pcm.SampleRate > 0 && pcm.Samples.Length > 0)
            {
                _introLength = TimeSpan.FromSeconds(pcm.Samples.Length / (double)pcm.SampleRate);

                // Play the siren at half volume: every sample scaled to 50% of its
                // amplitude. Re-encoded so the player still receives a plain WAV.
                var quieter = new short[pcm.Samples.Length];
                for (var i = 0; i < quieter.Length; i++)
                {
                    quieter[i] = (short)(pcm.Samples[i] * SirenVolume);
                }

                _sirenBytes = WavAudio.Write(new PcmAudio(quieter, pcm.SampleRate));
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or FormatException)
        {
            _sirenBytes = null;
        }
    }

    /// <summary>
    /// Plays the launch siren, whose fade-in and fade-out are baked into the
    /// audio so it rises and falls with the intro.
    /// </summary>
    private void PlaySiren()
    {
        var player = SoundPlayer;
        if (player is not null && player.IsAvailable && _sirenBytes is not null)
        {
            player.Play(_sirenBytes);
        }
    }

    private async Task PlayAsync(CancellationToken token)
    {
        // Everything is timed against this clock so the closing fade can be
        // placed to land exactly as the siren ends.
        var clock = Stopwatch.StartNew();

        // The glow comes up under everything so the field is never flat black.
        var glow = Animate(GlowFadeIn(), Glow, token);

        // Patrol lights run for the whole intro, in opposite phase — enough
        // cycles to cover the siren rather than a fixed four.
        var cycles = Math.Max(4, (int)Math.Ceiling(_introLength.TotalMilliseconds / 380.0));
        _ = Animate(PatrolFlash(TimeSpan.Zero, cycles), PatrolRedLight, token);
        _ = Animate(PatrolFlash(TimeSpan.FromMilliseconds(190), cycles), PatrolBlueLight, token);

        // Letters draw on, 60ms apart.
        var letters = DrawLettersAsync(token);

        await Task.WhenAll(glow, letters);
        token.ThrowIfCancellationRequested();

        // The lightbar pulses once.
        await PulseLightbarAsync(token);
        token.ThrowIfCancellationRequested();

        // Hold on the finished wordmark, patrol lights still washing over it,
        // until the siren is almost done — then lift and fade so the intro and
        // the siren end on the same beat.
        var fade = TimeSpan.FromMilliseconds(700);
        var holdUntil = _introLength - fade;
        var remaining = holdUntil - clock.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, token);
        }

        await Task.WhenAll(
            Animate(Lift(fade), Composition, token),
            Animate(FadeOut(fade), Root, token));
    }

    private async Task DrawLettersAsync(CancellationToken token)
    {
        // Fully qualified: ImplicitUsings brings in System.IO, whose Path
        // collides with the shape.
        Avalonia.Controls.Shapes.Path[] letters = [L0, L1, L2, L3, L4, L5, L6, L7];

        var draws = letters.Select((letter, index) =>
            Animate(LetterDraw(TimeSpan.FromMilliseconds(120 + (index * 60))), letter, token));

        await Task.WhenAll(draws);
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
    /// control instead â€” the same mechanism Reveal.axaml uses in XAML â€” and
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

    /// <summary>
    /// One side of the lightbar: a hard-edged double flash over a low standing
    /// wash. Two of these in opposite phase read as a real bar; a smooth sine
    /// fade reads as a lava lamp.
    /// </summary>
    /// <remarks>
    /// The floor is 0.18 rather than near-zero. A real bar is dark for most of
    /// its cycle, but at 1.4s total that produced isolated flickers against
    /// black rather than a lit scene â€” the eye never got long enough to read it
    /// as patrol lighting. Keeping a standing wash under the flashes reads
    /// correctly and still leaves the double-tap obvious.
    /// </remarks>
    private static Animation PatrolFlash(TimeSpan delay, int iterations) => new()
    {
        Duration = TimeSpan.FromMilliseconds(380),
        Delay = delay,
        IterationCount = new IterationCount((ulong)iterations),
        FillMode = FillMode.Both,
        Children =
        {
            Frame(0d, (OpacityProperty, 0.18d)),
            Frame(0.08d, (OpacityProperty, 0.95d)),
            Frame(0.20d, (OpacityProperty, 0.32d)),
            Frame(0.30d, (OpacityProperty, 0.90d)),
            Frame(0.46d, (OpacityProperty, 0.18d)),
            Frame(1d, (OpacityProperty, 0.18d)),
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

    private static Animation Lift(TimeSpan duration) => new()
    {
        Duration = duration,
        Easing = new CubicEaseIn(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, (TranslateTransform.YProperty, 0d)),
            Frame(1d, (TranslateTransform.YProperty, -8d)),
        },
    };

    private static Animation FadeOut(TimeSpan duration) => new()
    {
        Duration = duration,
        Easing = new SineEaseInOut(),
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

    // ===== Completion ====================================================

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
