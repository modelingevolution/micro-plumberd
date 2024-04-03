namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    Func<Type, IObjectSerializer> SerializerFactory { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
}