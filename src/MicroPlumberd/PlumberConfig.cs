using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

class PlumberConfig : IPlumberConfig
{
    internal readonly ConcurrentDictionary<Type, object> Extension = new();
    public T GetExtension<T>() where T : new() => (T)Extension.GetOrAdd(typeof(T), x => new T());

    private IServiceProvider _serviceProvider = new ActivatorServiceProvider();
    private static readonly JsonObjectSerializer serializer = new JsonObjectSerializer();
    private Func<Type,IObjectSerializer> _serializerFactory = x => serializer;
    private Func<Exception, string, CancellationToken, Task<ErrorHandleDecision>> _errorHandlePolicy = OnError;

    public Func<Type,IObjectSerializer> SerializerFactory
    {
        get => _serializerFactory;
        set
        {
            if(value == null!) throw new ArgumentNullException("ObjectSerializer cannot be null.");
            _serializerFactory = value;
        }
    }

    public Conventions Conventions { get; } = new Conventions();
    IConventions IPlumberConfig.Conventions => this.Conventions;

    public PlumberConfig()
    {
        Conventions.SnapshotPolicyFactoryConvention = OnSnapshotPolicy;
    }

    private ISnapshotPolicy? OnSnapshotPolicy(Type ownerType)
    {
        var t = ownerType.GetCustomAttribute<AggregateAttribute>()?.SnaphotPolicy;
        if (t != null)
        {
            if (t.IsGenericTypeDefinition) t = t.MakeGenericType(ownerType);
            return (ISnapshotPolicy)ServiceProvider.GetRequiredService(t);
        }
        return null;
    }

    public IServiceProvider ServiceProvider
    {
        get => _serviceProvider;
        set
        {
            if (value == null!) throw new ArgumentNullException("ServiceProvider cannot be null.");
            _serviceProvider = value;
        }
    }

    internal Func<Exception, string, CancellationToken, Task<ErrorHandleDecision>> ErrorHandlePolicy
    {
        get => _errorHandlePolicy;
    }

    public event Action<IPlumber>? Created;
    public void SetErrorHandlePolicy(Func<Exception, string, CancellationToken, Task<ErrorHandleDecision>> value)
    {
        if (value == null!) throw new ArgumentNullException();
        _errorHandlePolicy = value;
    }

    public event Action<IServiceCollection>? Configured;

    private static async Task<ErrorHandleDecision> OnError(Exception ex, string stream, CancellationToken token)
    {
        await Task.Delay(30000, token);
        return ErrorHandleDecision.Retry;
    }
    
    internal void OnConfigured(IServiceCollection collection)
    {
        Configured?.Invoke(collection);
    }
    internal void OnCreated(Plumber plumber)
    {
        Created?.Invoke(plumber);
    }

    
}