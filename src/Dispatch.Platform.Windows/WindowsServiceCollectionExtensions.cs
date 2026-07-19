using Dispatch.Core.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dispatch.Platform.Windows;

/// <summary>
/// Registers the Windows implementations of the platform interfaces declared
/// in Dispatch.Core.
/// </summary>
public static class WindowsServiceCollectionExtensions
{
    /// <summary>Adds every Windows platform service to <paramref name="services"/>.</summary>
    public static IServiceCollection AddDispatchWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICallsignVoice, SapiCallsignVoice>();

        // Implementations land here as the interfaces they satisfy are
        // introduced in Core: ProcessGuard, PowerService, ElevationService,
        // DefenderService, DependencyInstaller, WebView2BrowserSource.
        return services;
    }
}
