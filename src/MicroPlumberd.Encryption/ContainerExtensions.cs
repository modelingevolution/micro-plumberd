using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Encryption;

/// <summary>
/// Extension methods for configuring encryption services in the dependency injection container.
/// </summary>
public static class ContainerExtensions
{
    /// <summary>
    /// Adds encryption services to the service collection, including certificate management and encryption/decryption providers.
    /// </summary>
    /// <param name="services">The service collection to add encryption services to.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEncryption(this IServiceCollection services)
    {
        services.AddHostedService<CertManagerInitializer>();
        services.TryAddSingleton<IEncryptor, Encryptor>();
        services.TryAddSingleton<ICertManager, CertManager>();
        services.AddSingleton<PubCertEventHandler>();
        return services;
    }

    /// <summary>
    /// Enables encryption support in the Plumber configuration by registering the secret object JSON converter.
    /// </summary>
    /// <param name="services">The Plumber configuration to enable encryption for.</param>
    /// <returns>The Plumber configuration for method chaining.</returns>
    public static IPlumberConfig EnableEncryption(this IPlumberConfig services)
    {
        services.Created += (p) =>
        {
            if(!JsonObjectSerializer.Options.Converters.OfType<SecretConverterJsonConverterFactory>().Any())
                JsonObjectSerializer.Options.Converters.Add(new SecretConverterJsonConverterFactory(services.ServiceProvider));
        };
        return services;
    }
}