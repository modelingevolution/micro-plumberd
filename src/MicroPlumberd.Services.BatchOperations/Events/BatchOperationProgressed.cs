namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Event emitted when a batch operation reports progress.
/// </summary>
/// <param name="OperationId">The unique identifier of the operation.</param>
/// <param name="Current">The current progress count.</param>
/// <param name="Total">The total number of items to process.</param>
/// <param name="Message">An optional status message.</param>
[OutputStream("BatchOperations")]
public record BatchOperationProgressed(
    Guid OperationId,
    ulong Current,
    ulong Total,
    string? Message = null);
