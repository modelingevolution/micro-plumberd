using Microsoft.Extensions.Logging;

namespace MicroPlumberd;

class ActivatorServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
            return null;
        
        return Activator.CreateInstance(serviceType);
    }
}