namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Event emitted when a batch operation is cancelled.
/// </summary>
/// <param name="OperationId">The unique identifier of the operation.</param>
/// <param name="Reason">The reason for cancellation.</param>
[OutputStream("BatchOperations")]
public record BatchOperationCancelled(
    Guid OperationId,
    string Reason);
