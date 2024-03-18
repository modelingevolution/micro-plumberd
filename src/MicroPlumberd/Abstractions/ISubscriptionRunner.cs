namespace MicroPlumberd;

public interface ISubscriptionRunner : IAsyncDisposable
{
    Task<T> WithHandler<T>(T model) where T : IEventHandler, ITypeRegister;
    Task<T> WithHandler<T>(T model, TypeEventConverter mapFunc) where T : IEventHandler;
    Task<T> WithHandler<T>() where T : IEventHandler, ITypeRegister;
}