using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Dispatch.UI.Controls;

/// <summary>
/// One line of advice at a time, crossfading on a slow cycle, for the duration
/// of a long install.
/// </summary>
/// <remarks>
/// Tips are read from <c>Assets/tips.json</c> at runtime so they can be
/// rewritten without a rebuild.
///
/// <para>
/// The order is shuffled once per run rather than being cycled in file order.
/// A fifteen-minute install shows roughly a hundred tips, and a fixed order
/// makes the loop obvious the second time round.
/// </para>
/// </remarks>
public sealed class TipRail : TemplatedControl
{
    private static readonly Uri TipsAsset = new("avares://Dispatch.UI/Assets/tips.json");

    private readonly List<string> _tips = [];
    private CancellationTokenSource? _cancellation;
    private TextBlock? _text;
    private int _index;

    /// <summary>Defines the <see cref="Interval"/> property.</summary>
    public static readonly StyledProperty<TimeSpan> IntervalProperty =
        AvaloniaProperty.Register<TipRail, TimeSpan>(nameof(Interval), TimeSpan.FromSeconds(8));

    /// <summary>How long each tip is held before the next fades in.</summary>
    public TimeSpan Interval
    {
        get => GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _text = e.NameScope.Find<TextBlock>("PART_Tip");

        LoadTips();
        Restart();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void LoadTips()
    {
        if (_tips.Count > 0)
        {
            return;
        }

        // Malformed or missing tips must not break an install screen. The rail
        // simply stays empty.
        try
        {
            using var stream = AssetLoader.Open(TipsAsset);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("tips", out var tips) ||
                tips.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var tip in tips.EnumerateArray())
            {
                if (tip.GetString() is { Length: > 0 } value)
                {
                    _tips.Add(value);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or FileNotFoundException)
        {
            return;
        }

        Shuffle(_tips);
    }

    // Fisher-Yates. Shared.Next is fine here; nothing about tip order needs to
    // be reproducible.
    private static void Shuffle(List<string> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private void Restart()
    {
        if (_tips.Count == 0 || _text is null)
        {
            return;
        }

        _index = 0;
        _text.Text = _tips[0];
        _text.Opacity = 1;

        if (_tips.Count > 1)
        {
            _ = CycleAsync((_cancellation = new CancellationTokenSource()).Token);
        }
    }

    private async Task CycleAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(Interval, token);

                await Fade(_text!, 0, token);

                _index = (_index + 1) % _tips.Count;
                _text!.Text = _tips[_index];

                await Fade(_text, 1, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Screen went away mid-cycle.
        }
    }

    private static Task Fade(Visual target, double to, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420),
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, to > 0.5 ? 0d : 1d) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, to) },
                },
            },
        };

        return animation.RunAsync(target, token);
    }
}
