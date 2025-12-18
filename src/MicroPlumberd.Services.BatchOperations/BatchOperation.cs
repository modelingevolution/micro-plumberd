using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Represents a batch operation with its current state.
/// Implements INotifyPropertyChanged for reactive UI bindings.
/// </summary>
public class BatchOperation : INotifyPropertyChanged
{
    /// <summary>
    /// The possible states of a batch operation.
    /// </summary>
    public enum State
    {
        /// <summary>Operation has been created but not yet started.</summary>
        Initialized,
        /// <summary>Operation is currently running.</summary>
        Running,
        /// <summary>Operation completed successfully.</summary>
        Completed,
        /// <summary>Operation failed with an error.</summary>
        Failed,
        /// <summary>Operation was cancelled.</summary>
        Canceled
    }

    private State _status;
    private float _progress;
    private DateTimeOffset? _lastUpdateTime;
    private DateTimeOffset? _endTime;
    private string? _currentMessage;

    /// <summary>
    /// Gets or sets the unique identifier of the operation.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the type/name of the operation.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time when the operation started.
    /// </summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>
    /// Event raised when the operation completes.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Gets or sets the time of the last progress update.
    /// </summary>
    public DateTimeOffset? LastUpdateTime
    {
        get => _lastUpdateTime;
        set => SetField(ref _lastUpdateTime, value);
    }

    /// <summary>
    /// Gets or sets the time when the operation ended.
    /// </summary>
    public DateTimeOffset? EndTime
    {
        get => _endTime;
        set => SetField(ref _endTime, value);
    }

    /// <summary>
    /// Gets or sets the current status of the operation.
    /// </summary>
    public State Status
    {
        get => _status;
        set
        {
            if (!SetField(ref _status, value)) return;
            if (value == State.Completed) Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the estimated time of completion based on current progress and elapsed time.
    /// Returns null if ETA cannot be calculated.
    /// </summary>
    public DateTimeOffset? Eta
    {
        get
        {
            // If operation is completed, failed, or canceled, there's no ETA
            if (Status == State.Completed || Status == State.Failed || Status == State.Canceled)
                return null;

            // If progress is 0 or 1, we can't calculate ETA
            if (Progress <= 0 || Progress >= 1)
                return null;

            // Calculate ETA based on current progress and time elapsed
            TimeSpan elapsed = DateTimeOffset.Now - StartTime;
            double progressPercentage = Progress;

            // Avoid division by zero
            if (progressPercentage > 0)
            {
                // Calculate remaining time based on current rate of progress
                TimeSpan estimatedTotalTime = TimeSpan.FromTicks((long)(elapsed.Ticks / progressPercentage));
                TimeSpan remainingTime = estimatedTotalTime - elapsed;

                // Return current time plus remaining time
                return DateTimeOffset.Now.Add(remainingTime);
            }

            return null;
        }
    }

    /// <summary>
    /// Gets or sets the progress value between 0 and 1.
    /// </summary>
    public float Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    /// <summary>
    /// Gets or sets the current status message.
    /// </summary>
    public string? CurrentMessage
    {
        get => _currentMessage;
        set => SetField(ref _currentMessage, value);
    }

    /// <summary>
    /// Gets the duration of the operation.
    /// </summary>
    public TimeSpan Duration => EndTime.HasValue ? (EndTime.Value - StartTime) : (DateTimeOffset.Now - StartTime);

    /// <summary>
    /// Gets or sets the application context in which the operation was started.
    /// </summary>
    public AppContext AppContext { get; set; } = AppContext.Empty;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets a field value and raises PropertyChanged if the value changed.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
