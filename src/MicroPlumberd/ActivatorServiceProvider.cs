namespace MicroPlumberd;

class ActivatorServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        return Activator.CreateInstance(serviceType);
    }
}