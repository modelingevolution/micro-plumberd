using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MicroPlumberd.Encryption;

public static class ContainerExtensions
{
    public static IServiceCollection AddEncryption(this IServiceCollection services)
    {
        services.AddHostedService<CertManagerInitializer>();
        services.TryAddSingleton<IEncryptor, Encryptor>();
        services.TryAddSingleton<ICertManager, CertManager>();
        return services;
    }
    public static IPlumberConfig EnableEncryption(this IPlumberConfig services)
    {
        services.Created += (p) => { JsonObjectSerializer.Options.Converters.Add(new SecretConverterJsonConverterFactory(services.ServiceProvider)); };
        return services;
    }
}