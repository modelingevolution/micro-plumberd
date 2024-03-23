using System.Collections.Concurrent;

namespace MicroPlumberd;

class PlumberConfig : IPlumberConfig
{
    internal readonly ConcurrentDictionary<Type, object> Extension = new();
    public T GetExtension<T>() where T : new() => (T)Extension.GetOrAdd(typeof(T), x => new T());

    private IServiceProvider _serviceProvider = new ActivatorServiceProvider();
    private IObjectSerializer _serializer = new ObjectSerializer();

    public IObjectSerializer Serializer
    {
        get => _serializer;
        set
        {
            if(value == null!) throw new ArgumentNullException("ObjectSerializer cannot be null.");
            _serializer = value;
        }
    }

    public IConventions Conventions { get; } = new Conventions();

    public IServiceProvider ServiceProvider
    {
        get => _serviceProvider;
        set
        {
            if (value == null!) throw new ArgumentNullException("ServiceProvider cannot be null.");
            _serviceProvider = value;
        }
    }
}