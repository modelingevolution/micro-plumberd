namespace MicroPlumberd;

/// <summary>
/// Interface for creating snapshot policies, that manage when a snapshot is performed on an aggregate.
/// </summary>
public interface ISnapshotPolicy
{
    bool ShouldMakeSnapshot(object owner, StateInfo? info);
}