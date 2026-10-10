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
    /// Enables encryption support in the Plumber configuration: <see cref="SecretObject{T}"/> members are
    /// encrypted on write and decrypted on read with THIS configuration's <see cref="IEncryptor"/>.
    /// <para>
    /// The plumber gets a JSON serializer of its own (a copy of <see cref="JsonObjectSerializer.Options"/>
    /// plus the secret converter), built now, before anything can use it. The process-wide default options
    /// are never touched: they are read-only once used, and they are shared by plumbers that did not ask for
    /// encryption — or that belong to another container with other keys. Only the DEFAULT JSON serializer is
    /// replaced; a serializer the application chose is left as it is.
    /// </para>
    /// </summary>
    /// <param name="config">The Plumber configuration to enable encryption for.</param>
    /// <returns>The Plumber configuration for method chaining.</returns>
    public static IPlumberConfig EnableEncryption(this IPlumberConfig config)
    {
        var options = JsonObjectSerializer.CreateOptions();
        // The provider is read when a secret is first (de)serialized, not now: AddPlumberd may set it after
        // the configure callback ran.
        options.Converters.Add(new SecretConverterJsonConverterFactory(() => config.ServiceProvider));
        var encrypting = new JsonObjectSerializer(options);

        var previous = config.SerializerFactory;
        config.SerializerFactory = t => previous(t) is JsonObjectSerializer { UsesDefaultOptions: true }
            ? encrypting
            : previous(t);
        return config;
    }
}