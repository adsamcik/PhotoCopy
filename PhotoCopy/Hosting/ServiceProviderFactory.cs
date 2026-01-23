using Microsoft.Extensions.DependencyInjection;
using PhotoCopy.Commands;
using PhotoCopy.Configuration;
using PhotoCopy.Extensions;

namespace PhotoCopy.Hosting;

/// <summary>
/// Factory for building the application's service provider.
/// Centralizes DI container configuration.
/// </summary>
public static class ServiceProviderFactory
{
    /// <summary>
    /// Builds a service provider with PhotoCopy services configured.
    /// </summary>
    /// <param name="config">The PhotoCopy configuration.</param>
    /// <param name="diagnostics">Optional configuration diagnostics for tracking config sources.</param>
    /// <returns>A configured service provider.</returns>
    public static ServiceProvider Build(PhotoCopyConfig config, ConfigurationDiagnostics? diagnostics = null)
    {
        var services = new ServiceCollection();
        services.AddPhotoCopyServices(config, diagnostics);
        return services.BuildServiceProvider();
    }
}
