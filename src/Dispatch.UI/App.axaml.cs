using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Dispatch.UI.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.UI;

/// <summary>
/// The Avalonia application object. It resolves the shell from the container
/// and shows it; beyond that and the motion swap it holds no logic.
/// </summary>
public sealed class App : Application
{
    private static readonly Uri MotionUri = new("avares://Dispatch.UI/Theme/Motion.axaml");
    private static readonly Uri MotionReducedUri = new("avares://Dispatch.UI/Theme/MotionReduced.axaml");

    private readonly IServiceProvider? _services;

    /// <summary>Parameterless constructor required by the XAML previewer and headless tests.</summary>
    public App()
    {
    }

    /// <summary>Constructs the application against a composed container.</summary>
    public App(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Turns reduced motion on or off. Every duration in the application reads
    /// from the motion resource keys, so this single call is the whole feature
    /// — there is no condition checked at any individual animation site.
    /// </summary>
    /// <remarks>
    /// Implemented by layering MotionReduced.axaml on top of the merged
    /// dictionaries rather than by replacing Motion.axaml in place. Replacing
    /// an entry does not re-resolve already-merged keys, so the swap appears to
    /// work and changes nothing; appending an override does, because the last
    /// merged dictionary wins. Removing the override restores the base timings.
    /// </remarks>
    /// <param name="reduced">True to collapse every duration to zero.</param>
    public void SetReducedMotion(bool reduced)
    {
        var dictionaries = Resources.MergedDictionaries;

        var existing = dictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(include => include.Source == MotionReducedUri);

        if (reduced)
        {
            if (existing is null)
            {
                dictionaries.Add(new ResourceInclude(MotionReducedUri) { Source = MotionReducedUri });
            }
        }
        else if (existing is not null)
        {
            dictionaries.Remove(existing);
        }
    }
}
