using System.Runtime.CompilerServices;

namespace MicroPlumberd;

/// <summary>
/// Provides support for lazy initialization of a value in an async context
/// Uses TaskCompletionSource for efficient synchronization
/// </summary>
public class AsyncLazy<T>
{
    private readonly Func<Task<T>> _valueFactory;
    private T _value;
    private volatile bool _initialized;
    private readonly TaskCompletionSource<T> _tcs;

    /// <summary>
    /// Initializes a new instance of AsyncLazy with the specified value factory
    /// </summary>
    public AsyncLazy(Func<Task<T>> valueFactory)
    {
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Gets whether the value has been created
    /// </summary>
    public bool IsValueCreated => _initialized;

    /// <summary>
    /// Gets the lazily initialized value.
    /// Returns immediately if already initialized.
    /// </summary>
    public ValueTask<T> Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // Fast path - return immediately if initialized
            if (_initialized)
            {
                return new ValueTask<T>(_value);
            }

            return new ValueTask<T>(GetValueAsync());
        }
    }

    private async Task<T> GetValueAsync()
    {
        var currentAttempt = Interlocked.Increment(ref _initiatingThreads);

        // First thread handles initialization
        if (currentAttempt != 1) return await _tcs.Task;

        try
        {
            _value = await _valueFactory();
            _initialized = true;
            _tcs.SetResult(_value);
            return _value;
        }
        catch (Exception ex)
        {
            _tcs.SetException(ex);
            throw;
        }

    }

    private int _initiatingThreads = 0;
}