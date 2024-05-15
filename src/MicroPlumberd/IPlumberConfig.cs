using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
    event Action<IPlumber> Created;
}

public interface IPlumberReadOnlyConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; }
    IReadOnlyConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; }
}