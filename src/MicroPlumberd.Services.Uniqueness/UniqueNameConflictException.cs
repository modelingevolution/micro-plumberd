namespace MicroPlumberd.Services.Uniqueness;

/// <summary>Thrown when a name is already reserved within the category by a different source.</summary>
public class UniqueNameConflictException(string name, string category, Guid heldBy)
    : Exception($"Name '{name}' is already reserved in category '{category}' by {heldBy}.")
{
    /// <summary>The name that could not be reserved.</summary>
    public string Name { get; } = name;

    /// <summary>The uniqueness category (table) the clash occurred in.</summary>
    public string Category { get; } = category;

    /// <summary>The source (aggregate id) currently holding the name.</summary>
    public Guid HeldBy { get; } = heldBy;
}
