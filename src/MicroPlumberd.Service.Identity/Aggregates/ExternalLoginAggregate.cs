using System.Collections.Immutable;

namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Aggregate root managing external login providers for a user (e.g., Google, Microsoft, Facebook).
/// </summary>
[Aggregate]
[OutputStream("ExternalLogin")]
public partial class ExternalLoginAggregate : AggregateBase<UserIdentifier, ExternalLoginAggregate.ExternalLoginState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLoginAggregate"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    public ExternalLoginAggregate(UserIdentifier id) : base(id) { }

    /// <summary>
    /// Represents the state of all external logins associated with a user.
    /// </summary>
    public record ExternalLoginState
    {
        /// <summary>
        /// Gets the list of external login providers configured for this user.
        /// </summary>
        public ImmutableList<ExternalLoginRecord> Logins { get; init; } = ImmutableList<ExternalLoginRecord>.Empty;

        /// <summary>
        /// Gets a value indicating whether this external login aggregate has been deleted.
        /// </summary>
        public bool IsDeleted { get; init; }
    }

    /// <summary>
    /// Represents a single external login provider configuration for a user.
    /// </summary>
    public record ExternalLoginRecord
    {
        /// <summary>
        /// Gets the external login provider.
        /// </summary>
        public ExternalLoginProvider Provider { get; init; }

        /// <summary>
        /// Gets the unique identifier for the user within the external provider's system.
        /// </summary>
        public ExternalLoginKey ProviderKey { get; init; }

        /// <summary>
        /// Gets the display name for this external login.
        /// </summary>
        public string DisplayName { get; init; }
    }

    // Event application methods
    private static ExternalLoginState Given(ExternalLoginState state, ExternalLoginAggregateCreated ev)
    {
        return new ExternalLoginState
        {
            
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

    /// <summary>
    /// Creates a new external login aggregate for the specified user.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    /// <returns>A new <see cref="ExternalLoginAggregate"/> instance.</returns>
    public static ExternalLoginAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new ExternalLoginAggregateCreated());

        return aggregate;
    }

    /// <summary>
    /// Adds an external login provider for the user.
    /// </summary>
    /// <param name="provider">The external login provider. Cannot be empty.</param>
    /// <param name="providerKey">The user's unique identifier within the provider's system. Cannot be empty.</param>
    /// <param name="displayName">The display name for this external login.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify external logins of a deleted user.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="provider"/> or <paramref name="providerKey"/> is empty.</exception>
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
            
            Provider = provider,
            ProviderKey = providerKey,
            DisplayName = displayName ?? string.Empty,
            
        });
    }

    /// <summary>
    /// Removes an external login provider for the user.
    /// </summary>
    /// <param name="provider">The external login provider to remove.</param>
    /// <param name="providerKey">The provider-specific key to remove.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify external logins of a deleted user.</exception>
    public void RemoveLogin(ExternalLoginProvider provider, ExternalLoginKey providerKey)
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
            Provider = provider,
            ProviderKey = providerKey,
            
        });
    }



    /// <summary>
    /// Marks the external login aggregate as deleted.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted)
            return;


        AppendPendingChange(new ExternalLoginAggregateDeleted());
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify external logins of a deleted user");
    }

    
}