using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Dispatch.UI.Controls;

/// <summary>
/// A loading backdrop in the style the genre established: full-bleed stills
/// that crossfade into one another while a slow push keeps the frame alive.
/// </summary>
/// <remarks>
/// Images are discovered at runtime from <c>avares://Dispatch.UI/Assets/Loading</c>.
/// Anything dropped into that folder is compiled in by the project's
/// <c>AvaloniaResource</c> wildcard and ships inside the executable, so a
/// downloaded release has them without any extra files alongside it.
///
/// <para>
/// The folder is empty in the repository. Dispatch bundles no game or mod
/// artwork of its own — that material belongs to Rockstar and the mod authors.
/// With no images present this control renders nothing and the install screen
/// falls back to the vector scene, so the app is complete either way.
/// </para>
///
/// <para>
/// Two layers alternate rather than one layer swapping its source: a single
/// Image flashes its background between frames no matter how the fade is timed,
/// because the source changes instantly and the opacity does not.
/// </para>
/// </remarks>
public sealed class LoadingSlideshow : TemplatedControl
{
    private static readonly Uri LoadingFolder = new("avares://Dispatch.UI/Assets/Loading");

    private readonly List<Bitmap> _frames = [];
    private CancellationTokenSource? _cancellation;
    private Image? _layerA;
    private Image? _layerB;
    private int _index;
    private bool _showingA = true;

    /// <summary>Defines the <see cref="Interval"/> property.</summary>
    public static readonly StyledProperty<TimeSpan> IntervalProperty =
        AvaloniaProperty.Register<LoadingSlideshow, TimeSpan>(
            nameof(Interval), TimeSpan.FromSeconds(8));

    /// <summary>Defines the <see cref="HasImages"/> property.</summary>
    public static readonly DirectProperty<LoadingSlideshow, bool> HasImagesProperty =
        AvaloniaProperty.RegisterDirect<LoadingSlideshow, bool>(
            nameof(HasImages), o => o.HasImages);

    private bool _hasImages;

    /// <summary>How long each still is held before the next one fades in.</summary>
    public TimeSpan Interval
    {
        get => GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    /// <summary>
    /// Whether any stills were found. False means the caller should show its
    /// own fallback rather than a black rectangle.
    /// </summary>
    public bool HasImages
    {
        get => _hasImages;
        private set => SetAndRaise(HasImagesProperty, ref _hasImages, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _layerA = e.NameScope.Find<Image>("PART_LayerA");
        _layerB = e.NameScope.Find<Image>("PART_LayerB");

        LoadFrames();
        Restart();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    private void LoadFrames()
    {
        if (_frames.Count > 0)
        {
            return;
        }

        // A missing folder is the normal case in a fresh clone, and a corrupt
        // drop-in must not take the install screen down over decoration.
        try
        {
            var assets = AssetLoader.GetAssets(LoadingFolder, null)
                .Where(IsImage)
                .OrderBy(uri => uri.AbsolutePath, StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets)
            {
                try
                {
                    _frames.Add(new Bitmap(AssetLoader.Open(asset)));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
                {
                    // Skip the one bad file, keep the rest.
                }
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // No folder, no slideshow. The caller falls back.
        }

        HasImages = _frames.Count > 0;
    }

    private static bool IsImage(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private void Restart()
    {
        Stop();

        if (_frames.Count == 0 || _layerA is null || _layerB is null)
        {
            return;
        }

        _index = 0;
        _showingA = true;
        _layerA.Source = _frames[0];
        _layerA.Opacity = 1;
        _layerB.Opacity = 0;

        // A single still needs no cycle, but still gets the slow push.
        _ = RunAsync((_cancellation = new CancellationTokenSource()).Token);
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            // The push runs continuously under the crossfades rather than
            // restarting per frame, so the motion never visibly resets.
            _ = KenBurns(_layerA!, token);
            _ = KenBurns(_layerB!, token);

            while (_frames.Count > 1 && !token.IsCancellationRequested)
            {
                await Task.Delay(Interval, token);

                _index = (_index + 1) % _frames.Count;

                var incoming = _showingA ? _layerB! : _layerA!;
                var outgoing = _showingA ? _layerA! : _layerB!;

                incoming.Source = _frames[_index];

                await Task.WhenAll(
                    FadeTo(incoming, 1, token),
                    FadeTo(outgoing, 0, token));

                _showingA = !_showingA;
            }
        }
        catch (OperationCanceledException)
        {
            // Screen went away mid-cycle.
        }
    }

    private static Task FadeTo(Animatable target, double opacity, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1200),
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, opacity > 0.5 ? 0d : 1d) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, opacity) },
                },
            },
        };

        return animation.RunAsync((Visual)target, token);
    }

    private static Task KenBurns(Visual target, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(26),
            Easing = new SineEaseInOut(),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0d),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.12d),
                        new Setter(ScaleTransform.ScaleYProperty, 1.12d),
                    },
                },
            },
        };

        return animation.RunAsync(target, token);
    }
}
