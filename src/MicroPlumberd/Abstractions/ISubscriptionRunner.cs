namespace MicroPlumberd;

public interface ISubscriptionRunner : IAsyncDisposable
{
    Task<T> WithHandler<T>(T model) where T : IEventHandler, ITypeRegister;
    Task<T> WithHandler<T>(T model, TypeEventConverter mapFunc) where T : IEventHandler;
    Task<IEventHandler> WithHandler<T>() where T : IEventHandler, ITypeRegister;
    Task<IEventHandler> WithHandler<T>(TypeEventConverter mapFunc) where T : IEventHandler;
    Task<IEventHandler> WithHandler<T>(ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister;
    Task<IEventHandler> WithSnapshotHandler<T>() where T : IEventHandler, ITypeRegister;
    Task<IEventHandler> WithSnapshotHandler<T>(T model) where T : IEventHandler, ITypeRegister;
    Task<IEventHandler> WithHandler<T>(T model, ITypeHandlerRegisters register) where T : IEventHandler, ITypeRegister;
}