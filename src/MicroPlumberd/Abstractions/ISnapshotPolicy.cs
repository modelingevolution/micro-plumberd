namespace MicroPlumberd;

/// <summary>
/// Interface for creating snapshot policies, that manage when a snapshot is performed on an aggregate.
/// </summary>
public interface ISnapshotPolicy
{
    /// <summary>
    /// Determines whether a snapshot should be created for the specified owner object.
    /// </summary>
    /// <param name="owner">The owner object to evaluate for snapshot creation.</param>
    /// <param name="info">Optional state information containing version and creation time of last snapshot.</param>
    /// <returns><c>true</c> if a snapshot should be created; otherwise, <c>false</c>.</returns>
    bool ShouldMakeSnapshot(object owner, StateInfo? info);
}