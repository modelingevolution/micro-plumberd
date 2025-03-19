using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

public interface IServiceTypeRegister
{
    static abstract IEnumerable<Type> ReturnTypes { get; }
    static abstract IEnumerable<Type> FaultTypes { get; }
    static abstract IEnumerable<Type> CommandTypes { get; }
    static abstract IServiceCollection RegisterHandlers(IServiceCollection services, bool scoped = true);
}