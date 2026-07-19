using Dispatch.UI.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Dispatch.UI;

/// <summary>Registers views and view models. Called from the composition root.</summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>Adds every Dispatch.UI service to <paramref name="services"/>.</summary>
    public static IServiceCollection AddDispatchUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MainWindow>();

        return services;
    }
}
