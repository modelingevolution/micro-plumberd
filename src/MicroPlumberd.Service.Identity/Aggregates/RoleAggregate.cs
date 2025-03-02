namespace MicroPlumberd.Service.Identity.Aggregates;

[Aggregate]
public partial class RoleAggregate : AggregateBase<RoleIdentifier, RoleAggregate.RoleState>
{
    public RoleAggregate(RoleIdentifier id) : base(id) { }

    public record RoleState
    {
        public RoleIdentifier Id { get; init; }
        public string Name { get; init; }
        public string NormalizedName { get; init; }
        public string ConcurrencyStamp { get; init; }
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
            ConcurrencyStamp = ev.ConcurrencyStamp,
            IsDeleted = false
        };
    }

    private static RoleState Given(RoleState state, RoleNameChanged ev)
    {
        return state with
        {
            Name = ev.Name,
            NormalizedName = ev.NormalizedName,
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static RoleState Given(RoleState state, RoleConcurrencyStampChanged ev)
    {
        return state with { ConcurrencyStamp = ev.ConcurrencyStamp };
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
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });

        return aggregate;
    }

    public void ChangeName(string name, string normalizedName, string expectedConcurrencyStamp)
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted role");

        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Normalized role name cannot be empty", nameof(normalizedName));

        AppendPendingChange(new RoleNameChanged
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizedName,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void UpdateConcurrencyStamp()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted role");

        AppendPendingChange(new RoleConcurrencyStampChanged
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void Delete(string expectedConcurrencyStamp)
    {
        if (State.IsDeleted)
            return;

        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        AppendPendingChange(new RoleDeleted
        {
            Id = Guid.NewGuid()
        });
    }

    private void ValidateConcurrencyStamp(string expectedConcurrencyStamp)
    {
        if (expectedConcurrencyStamp != null &&
            State.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            throw new ConcurrencyException("Role was modified by another process");
        }
    }

}

