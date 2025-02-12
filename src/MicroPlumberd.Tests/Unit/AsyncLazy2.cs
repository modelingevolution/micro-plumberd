using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MicroPlumberd.Tests.Unit
{
    

    
        public class AsyncLazyTests2
        {
            [Fact]
            public void Constructor_NullFactory_ThrowsArgumentNullException()
            {
                Assert.Throws<ArgumentNullException>(() => new AsyncLazy<int>(null));
            }

            [Fact]
            public async Task Value_NotInitialized_ReturnsFalse()
            {
                var lazy = new AsyncLazy<int>(() => Task.FromResult(42));
                Assert.False(lazy.IsValueCreated);
            }

            [Fact]
            public async Task Value_AfterInitialization_ReturnsTrue()
            {
                var lazy = new AsyncLazy<int>(() => Task.FromResult(42));
                await lazy.Value;
                Assert.True(lazy.IsValueCreated);
            }

            [Fact]
            public async Task Value_SingleThread_InitializesOnce()
            {
                var initCount = 0;
                var lazy = new AsyncLazy<int>(async () =>
                {
                    Interlocked.Increment(ref initCount);
                    await Task.Delay(10);
                    return 42;
                });

                var value1 = await lazy.Value;
                var value2 = await lazy.Value;
                var value3 = await lazy.Value;

                Assert.Equal(42, value1);
                Assert.Equal(42, value2);
                Assert.Equal(42, value3);
                Assert.Equal(1, initCount);
            }

            [Fact]
            public async Task Value_MultipleThreads_InitializesOnce()
            {
                var initCount = 0;
                var lazy = new AsyncLazy<int>(async () =>
                {
                    Interlocked.Increment(ref initCount);
                    await Task.Delay(100); // Long enough to ensure concurrent access
                    return 42;
                });

                var tasks = new List<Task<int>>();
                for (int i = 0; i < 10; i++)
                {
                    tasks.Add(Task.Run(async () => await lazy.Value));
                }

                await Task.WhenAll(tasks);

                foreach (var task in tasks)
                {
                    Assert.Equal(42, await task);
                }
                Assert.Equal(1, initCount);
            }

            [Fact]
            public async Task Value_FactoryThrows_PropagatesException()
            {
                var exception = new InvalidOperationException("Test exception");
                var lazy = new AsyncLazy<int>(() => Task.FromException<int>(exception));

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => lazy.Value.AsTask());
                Assert.Same(exception, ex);
            }

            [Fact]
            public async Task Value_FactoryThrows_PropagatesSameExceptionToAllWaiters()
            {
                var exception = new InvalidOperationException("Test exception");
                var lazy = new AsyncLazy<int>(async () =>
                {
                    await Task.Delay(10);
                    throw exception;
                });

                var tasks = new List<Task>();
                for (int i = 0; i < 5; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => lazy.Value.AsTask());
                        Assert.Same(exception, ex);
                    }));
                }

                await Task.WhenAll(tasks);
            }

            [Fact]
            public async Task Value_AfterInitialization_ReturnsImmediately()
            {
                var lazy = new AsyncLazy<int>(async () =>
                {
                    await Task.Delay(1100);
                    return 42;
                });

                // First access - should take time
                var sw = Stopwatch.StartNew();
                await lazy.Value;
                var firstAccessTime = sw.ElapsedMilliseconds;

                // Second access - should be immediate
                sw.Restart();
                await lazy.Value;
                var secondAccessTime = sw.ElapsedMilliseconds;

                Assert.True(firstAccessTime >= 1000, "First access should take at least 1000ms");
                Assert.True(secondAccessTime < 50, "Second access should be nearly immediate");
            }

            [Fact]
            public async Task Value_ConcurrentAccess_AllThreadsGetSameValue()
            {
                var random = new Random();
                var lazy = new AsyncLazy<int>(async () =>
                {
                    await Task.Delay(100);
                    return random.Next();
                });

                var tasks = new Task<int>[10];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(async () => await lazy.Value);
                }

                var results = await Task.WhenAll(tasks);
                var firstValue = results[0];
                foreach (var result in results)
                {
                    Assert.Equal(firstValue, result);
                }
            }

            [Fact]
            public async Task Value_LongRunningTask_DoesNotBlockOtherThreads()
            {
                var initStarted = new TaskCompletionSource<bool>();
                var lazy = new AsyncLazy<int>(async () =>
                {
                    initStarted.SetResult(true);
                    await Task.Delay(1000);
                    return 42;
                });

                // Start the initialization
                var initTask = Task.Run(async () => await lazy.Value);

                // Wait for initialization to start
                await initStarted.Task;

                // Try to get value from other threads
                var tasks = new List<Task>();
                for (int i = 0; i < 5; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var value = await lazy.Value;
                        Assert.Equal(42, value);
                    }));
                }

                await Task.WhenAll(tasks.Concat(new[] { initTask }));
            }
        
    }
}
