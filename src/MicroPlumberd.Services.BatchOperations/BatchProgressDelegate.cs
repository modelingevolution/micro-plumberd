namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Delegate for reporting batch operation progress.
/// </summary>
/// <param name="current">The current progress count.</param>
/// <param name="total">The total number of items to process.</param>
/// <param name="message">An optional status message.</param>
/// <returns>A task representing the async operation.</returns>
public delegate Task BatchProgressDelegate(ulong current, ulong total, string? message = null);
