using System.Collections.Immutable;

namespace MicroPlumberd.Service.Identity.Aggregates;

[Aggregate]
public partial class ExternalLoginAggregate : AggregateBase<UserIdentifier, ExternalLoginAggregate.ExternalLoginState>
{
    public ExternalLoginAggregate(UserIdentifier id) : base(id) { }

    public record ExternalLoginState
    {
        public UserIdentifier Id { get; init; }
        public ImmutableList<ExternalLoginRecord> Logins { get; init; } = ImmutableList<ExternalLoginRecord>.Empty;
        
        public bool IsDeleted { get; init; }
    }

    public record ExternalLoginRecord
    {
        public ExternalLoginProvider Provider { get; init; }
        public ExternalLoginKey ProviderKey { get; init; }
        public string DisplayName { get; init; }
    }

    // Event application methods
    private static ExternalLoginState Given(ExternalLoginState state, ExternalLoginAggregateCreated ev)
    {
        return new ExternalLoginState
        {
            Id = ev.UserId,
            
            IsDeleted = false
        };
    }

    private static ExternalLoginState Given(ExternalLoginState state, ExternalLoginAdded ev)
    {
        var loginRecord = new ExternalLoginRecord
        {
            Provider = ev.Provider,
            ProviderKey = ev.ProviderKey,
            DisplayName = ev.DisplayName
        };

        // Check if login already exists
        bool loginExists = state.Logins.Any(l =>
            l.Provider.Name == ev.Provider.Name &&
            l.ProviderKey.Value == ev.ProviderKey.Value);

        if (loginExists)
            return state;

        return state with
        {
            Logins = state.Logins.Add(loginRecord),
            
        };
    }

    private static ExternalLoginState Given(ExternalLoginState state, ExternalLoginRemoved ev)
    {
        var loginToRemove = state.Logins.FirstOrDefault(l =>
            l.Provider.Name == ev.Provider.Name &&
            l.ProviderKey.Value == ev.ProviderKey.Value);

        if (loginToRemove == null)
            return state;

        return state with
        {
            Logins = state.Logins.Remove(loginToRemove),
            
        };
    }

    

    private static ExternalLoginState Given(ExternalLoginState state, ExternalLoginAggregateDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    // Command methods
    public static ExternalLoginAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new ExternalLoginAggregateCreated
        {
            Id = Guid.NewGuid(),
            UserId = id,
            
        });

        return aggregate;
    }

    public void AddLogin(
        ExternalLoginProvider provider,
        ExternalLoginKey providerKey,
        string displayName
        )
    {
        EnsureNotDeleted();
        

        // Validation
        if (string.IsNullOrEmpty(provider.Name))
            throw new ArgumentException("Provider name cannot be empty", nameof(provider));

        if (string.IsNullOrEmpty(providerKey.Value))
            throw new ArgumentException("Provider key cannot be empty", nameof(providerKey));

        // Check if login already exists
        bool loginExists = State.Logins.Any(l =>
            l.Provider.Name == provider.Name &&
            l.ProviderKey.Value == providerKey.Value);

        if (loginExists)
            return; // Login already added

        AppendPendingChange(new ExternalLoginAdded
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ProviderKey = providerKey,
            DisplayName = displayName ?? string.Empty,
            
        });
    }

    public void RemoveLogin(
        ExternalLoginProvider provider,
        ExternalLoginKey providerKey
        )
    {
        EnsureNotDeleted();
        

        // Check if login exists
        bool loginExists = State.Logins.Any(l =>
            l.Provider.Name == provider.Name &&
            l.ProviderKey.Value == providerKey.Value);

        if (!loginExists)
            return; // Login doesn't exist

        AppendPendingChange(new ExternalLoginRemoved
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ProviderKey = providerKey,
            
        });
    }

   

    public void Delete()
    {
        if (State.IsDeleted)
            return;

        

        AppendPendingChange(new ExternalLoginAggregateDeleted
        {
            Id = Guid.NewGuid()
        });
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify external logins of a deleted user");
    }

    
}