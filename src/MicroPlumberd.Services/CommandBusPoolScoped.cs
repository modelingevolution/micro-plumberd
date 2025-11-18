using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Services;

/// <summary>
/// Provides a scoped command bus pool that creates command buses within a dedicated service scope.
/// </summary>
sealed class CommandBusPoolScoped(IServiceProvider sp, int maxCount) : CommandBusPool(sp, maxCount)
{
    private readonly IServiceScope _scope = sp.CreateScope();

    /// <summary>
    /// Creates the specified number of command bus instances within the scoped service provider.
    /// </summary>
    /// <param name="number">The number of command bus instances to create.</param>
    /// <returns>An enumerable of command bus instances.</returns>
    public override IEnumerable<ICommandBus> Create(int number)
    {
        for (int i = 0; i < _maxCount; ++i)
            yield return _scope.ServiceProvider.GetRequiredService<ICommandBus>();
    }

}