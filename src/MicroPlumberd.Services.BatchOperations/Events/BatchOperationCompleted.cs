namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Event emitted when a batch operation completes (successfully or with failure).
/// </summary>
/// <param name="OperationId">The unique identifier of the operation.</param>
/// <param name="Success">Whether the operation completed successfully.</param>
/// <param name="Message">A completion message or error description.</param>
[OutputStream("BatchOperations")]
public record BatchOperationCompleted(
    Guid OperationId,
    bool Success,
    string Message);
