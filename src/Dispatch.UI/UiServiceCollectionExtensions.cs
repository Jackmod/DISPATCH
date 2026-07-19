using Dispatch.Core.Imagery;
using Dispatch.UI.Shell;
using Dispatch.UI.Wizard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dispatch.UI;

/// <summary>Registers views and view models. Called from the composition root.</summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>Adds every Dispatch.UI service to <paramref name="services"/>.</summary>
    public static IServiceCollection AddDispatchUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUserBackgrounds, UserBackgrounds>();
        services.AddSingleton<WizardViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
