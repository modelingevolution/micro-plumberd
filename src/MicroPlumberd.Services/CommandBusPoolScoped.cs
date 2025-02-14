using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

sealed class CommandBusPoolScoped(IServiceProvider sp, int maxCount) : CommandBusPool(sp, maxCount)
{
    private readonly IServiceScope _scope = sp.CreateScope();

    public override IEnumerable<ICommandBus> Create(int number)
    {
        for (int i = 0; i < _maxCount; ++i)
            yield return _scope.ServiceProvider.GetRequiredService<ICommandBus>();
    }

}