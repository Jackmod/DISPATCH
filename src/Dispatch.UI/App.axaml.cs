using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dispatch.UI.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.UI;

/// <summary>
/// The Avalonia application object. It resolves the shell from the container
/// and shows it; it contains no logic of its own beyond that.
/// </summary>
public sealed class App : Application
{
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
}
