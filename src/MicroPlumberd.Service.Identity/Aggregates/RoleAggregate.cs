namespace MicroPlumberd.Services.Identity.Aggregates;

[Aggregate]
public partial class RoleAggregate : AggregateBase<RoleIdentifier, RoleAggregate.RoleState>
{
    public RoleAggregate(RoleIdentifier id) : base(id) { }

    public record RoleState
    {
        public RoleIdentifier Id { get; init; }
        public string Name { get; init; }
        public string NormalizedName { get; init; }
        
        public bool IsDeleted { get; init; }
    }

    // Event application methods
    private static RoleState Given(RoleState state, RoleCreated ev)
    {
        return new RoleState
        {
            Id = ev.RoleId,
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

    // Command methods
    public static RoleAggregate Create(RoleIdentifier id, string name, string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Normalized role name cannot be empty", nameof(normalizedName));

        var aggregate = Empty(id);
        aggregate.AppendPendingChange(new RoleCreated
        {
            Id = Guid.NewGuid(),
            RoleId = id,
            Name = name,
            NormalizedName = normalizedName,
            
        });

        return aggregate;
    }

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
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = normalizedName
            });
        }
    }



    public void Delete()
    {
        if (State.IsDeleted)
            return;

        
        AppendPendingChange(new RoleDeleted
        {
            Id = Guid.NewGuid()
        });
    }

}

