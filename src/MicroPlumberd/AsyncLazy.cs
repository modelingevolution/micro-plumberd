using System.Runtime.CompilerServices;

namespace MicroPlumberd;

/// <summary>
/// Provides support for lazy initialization of a value in an async context
/// Uses TaskCompletionSource for efficient synchronization
/// </summary>
/// <remarks>
/// One attempt at a time: the attempt field changes only under a short lock and each caller takes it into a local
/// there; the caller that installed the attempt runs the factory outside the lock and completes that attempt.
/// (feature-017 design.md § slice B1, "MicroPlumberd/AsyncLazy.cs".)
/// </remarks>
public class AsyncLazy<T>
{
    private readonly Func<Task<T>> _valueFactory;
    private readonly bool _cacheFailure;
    private readonly object _gate = new();
    private T _value;
    private volatile bool _initialized;
    private TaskCompletionSource<T>? _attempt;

    /// <summary>
    /// Initializes a new instance of AsyncLazy with the specified value factory. A failed factory call stays cached:
    /// every later <see cref="Value"/> throws its exception.
    /// </summary>
    /// <param name="valueFactory">Creates the value.</param>
    // Kept as its own constructor (not an optional parameter on the one below): assemblies built against 1.3.9 bind to
    // this exact signature.
    public AsyncLazy(Func<Task<T>> valueFactory) : this(valueFactory, cacheFailure: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of AsyncLazy with the specified value factory and failure caching.
    /// </summary>
    /// <param name="valueFactory">Creates the value.</param>
    /// <param name="cacheFailure">
    /// <c>true</c>: a failed factory call stays cached and every later <see cref="Value"/> throws its exception.
    /// <c>false</c>: a failed or canceled attempt is not cached; the next <see cref="Value"/> runs the factory again.
    /// </param>
    public AsyncLazy(Func<Task<T>> valueFactory, bool cacheFailure)
    {
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _cacheFailure = cacheFailure;
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
        TaskCompletionSource<T> attempt;
        bool owner = false;
        lock (_gate)
        {
            if (_attempt is null || (!_cacheFailure && (_attempt.Task.IsFaulted || _attempt.Task.IsCanceled)))
            {
                _attempt = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                owner = true;
            }
            attempt = _attempt;
        }

        if (!owner) return await attempt.Task;

        try
        {
            _value = await _valueFactory();
            _initialized = true;
            attempt.SetResult(_value);
            return _value;
        }
        catch (Exception ex)
        {
            attempt.SetException(ex);
            throw;
        }
    }
}
