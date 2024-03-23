namespace MicroPlumberd;

public interface IPlumberConfig : IExtension
{
    IObjectSerializer Serializer { get; set; }
    IConventions Conventions { get; }
    IServiceProvider ServiceProvider { get; set; }
}