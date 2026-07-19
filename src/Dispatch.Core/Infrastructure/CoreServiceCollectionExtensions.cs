using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dispatch.Core.Infrastructure;

/// <summary>
/// Registers the domain services. The composition root calls this; nothing
/// else in Core knows the container exists.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>Adds every Dispatch.Core service to <paramref name="services"/>.</summary>
    public static IServiceCollection AddDispatchCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd throughout: the composition root may have already supplied a
        // rooted IAppPaths, and a test host certainly will.
        services.TryAddSingleton<IAppPaths, AppPaths>();

        return services;
    }
}
