namespace MicroPlumberd;

public interface ISubscriptionRunner : IAsyncDisposable
{
    Task WithModel<T>(T model) where T : IReadModel, ITypeRegister;
}