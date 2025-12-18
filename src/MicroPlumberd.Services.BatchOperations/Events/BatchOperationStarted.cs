namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Event emitted when a batch operation starts.
/// </summary>
/// <param name="OperationId">The unique identifier of the operation.</param>
/// <param name="OperationType">The type/name of the operation being performed.</param>
/// <param name="Start">The starting value of the range being processed.</param>
/// <param name="EndInclusive">The ending value (inclusive) of the range being processed.</param>
/// <param name="AppContext">The application context in which the operation was started.</param>
[OutputStream("BatchOperations")]
public record BatchOperationStarted(
    Guid OperationId,
    string OperationType,
    double Start,
    double EndInclusive,
    AppContext AppContext);
