using MicroPlumberd.Tests.Utils;

namespace MicroPlumberd.Tests.Unit;

[TestCategory("Unit")]
public class AsyncLazyTests
{
    [Fact]
    public async Task AsyncLazy_ReturnsValue_WhenAccessed()
    {
        // Arrange
        var expected = 42;
        var asyncLazy = new AsyncLazy<int>(() => Task.FromResult(expected));

        // Act
        var result = await asyncLazy;

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task AsyncLazy_OnlyExecutesValueFactoryOnce()
    {
        // Arrange
        int executionCount = 0;
        var asyncLazy = new AsyncLazy<int>(() =>
        {
            executionCount++;
            return Task.FromResult(42);
        });

        // Act
        var result1 = await asyncLazy;
        var result2 = await asyncLazy;

        // Assert
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task AsyncLazy_HandlesExceptions()
    {
        // Arrange
        var asyncLazy = new AsyncLazy<int>(new Func<Task<int>>( () => throw new InvalidOperationException()));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await asyncLazy);
    }

    [Fact]
    public async Task AsyncLazy_DoesNotStartUntilAccessed()
    {
        // Arrange
        bool isStarted = false;
        var asyncLazy = new AsyncLazy<int>(() =>
        {
            isStarted = true;
            return Task.FromResult(42);
        });

        // Act (not accessing the AsyncLazy instance yet)
        await Task.Delay(100); // Delay to ensure any auto-start would trigger

        // Assert
        Assert.False(isStarted);

        // Act (now accessing the AsyncLazy instance)
        await asyncLazy;

        // Assert
        Assert.True(isStarted);
    }
}