namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Aggregate root managing role information in the identity system.
/// </summary>
[Aggregate]
[OutputStream("Role")]
public partial class RoleAggregate : AggregateBase<RoleIdentifier, RoleAggregate.RoleState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoleAggregate"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the role.</param>
    public RoleAggregate(RoleIdentifier id) : base(id) { }

    /// <summary>
    /// Represents the state of a role.
    /// </summary>
    public record RoleState
    {
        /// <summary>
        /// Gets the name of the role.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the normalized name of the role for case-insensitive comparisons.
        /// </summary>
        public string NormalizedName { get; init; }

        /// <summary>
        /// Gets a value indicating whether this role has been deleted.
        /// </summary>
        public bool IsDeleted { get; init; }
    }

    // Event application methods
    private static RoleState Given(RoleState state, RoleCreated ev)
    {
        return new RoleState
        {
            Name = ev.Name,
            NormalizedName = ev.NormalizedName,
            
            IsDeleted = false
        };
    }

    private static RoleState Given(RoleState state, RoleNameChanged ev)
    {
        return state with
        {
            Name = ev.Name,
            NormalizedName = ev.NormalizedName,
            
        };
    }

   

    private static RoleState Given(RoleState state, RoleDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    /// <summary>
    /// Creates a new role aggregate with the specified name.
    /// </summary>
    /// <param name="id">The unique identifier for the role.</param>
    /// <param name="name">The name of the role. Cannot be null or whitespace.</param>
    /// <param name="normalizedName">The normalized name of the role. Cannot be null or whitespace.</param>
    /// <returns>A new <see cref="RoleAggregate"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> or <paramref name="normalizedName"/> is null or whitespace.</exception>
    public static RoleAggregate Create(RoleIdentifier id, string name, string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Normalized role name cannot be empty", nameof(normalizedName));

        var aggregate = Empty(id);
        aggregate.AppendPendingChange(new RoleCreated
        {
            Name = name,
            NormalizedName = normalizedName,
            
        });

        return aggregate;
    }

    /// <summary>
    /// Changes the name of the role.
    /// </summary>
    /// <param name="name">The new name for the role. Cannot be null or whitespace.</param>
    /// <param name="normalizedName">The new normalized name for the role. Cannot be null or whitespace.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify a deleted role.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> or <paramref name="normalizedName"/> is null or whitespace.</exception>
    public void ChangeName(string name, string normalizedName)
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted role");

        // Only emit an event if the name has actually changed
        if (State.Name != name || State.NormalizedName != normalizedName)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Role name cannot be empty", nameof(name));

            if (string.IsNullOrWhiteSpace(normalizedName))
                throw new ArgumentException("Normalized role name cannot be empty", nameof(normalizedName));

            AppendPendingChange(new RoleNameChanged
            {
                
                Name = name,
                NormalizedName = normalizedName
            });
        }
    }



    /// <summary>
    /// Marks the role as deleted.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted)
            return;

        AppendPendingChange(new RoleDeleted { });
    }

}

