using Microsoft.AspNetCore.Identity;
using System.Collections.Immutable;

namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Aggregate root managing authentication tokens for a user including refresh tokens, recovery codes, and provider-specific tokens.
/// </summary>
[Aggregate]
[OutputStream("Token")]
public partial class TokenAggregate : AggregateBase<UserIdentifier, TokenAggregate.TokenState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenAggregate"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    public TokenAggregate(UserIdentifier id) : base(id) { }

    /// <summary>
    /// Represents the state of all tokens associated with a user.
    /// </summary>
    public record TokenState
    {
        /// <summary>
        /// Gets the list of tokens for this user.
        /// </summary>
        public ImmutableList<TokenRecord> Tokens { get; init; } = ImmutableList<TokenRecord>.Empty;

        /// <summary>
        /// Gets a value indicating whether this token aggregate has been deleted.
        /// </summary>
        public bool IsDeleted { get; init; }
    }

    /// <summary>
    /// Represents a single token record with name, value, and login provider association.
    /// </summary>
    public record TokenRecord
    {
        /// <summary>
        /// Gets the name of the token.
        /// </summary>
        public TokenName Name { get; init; }

        /// <summary>
        /// Gets the value of the token.
        /// </summary>
        public TokenValue Value { get; init; }

        /// <summary>
        /// Gets the login provider associated with this token, if any.
        /// </summary>
        public string LoginProvider { get; init; }
    }

    // Event application methods
    private static TokenState Given(TokenState state, TokenAggregateCreated ev)
    {
        
        return new TokenState
        {
            IsDeleted = false
        };
    }

    private static TokenState Given(TokenState state, TokenSet ev)
    {
        // Remove existing token with the same name and login provider
        var existingToken = state.Tokens.FirstOrDefault(t =>
            t.Name.Value == ev.Name.Value &&
            t.LoginProvider == ev.LoginProvider);

        var tokens = state.Tokens;

        if (existingToken != null)
        {
            tokens = tokens.Remove(existingToken);
        }

        // Add the new token
        var tokenRecord = new TokenRecord
        {
            Name = ev.Name,
            Value = ev.Value,
            LoginProvider = ev.LoginProvider
        };

        return state with
        {
            Tokens = tokens.Add(tokenRecord),
            
        };
    }

    private static TokenState Given(TokenState state, TokenRemoved ev)
    {
        // Find the token to remove
        var tokenToRemove = state.Tokens.FirstOrDefault(t =>
            t.Name.Value == ev.Name.Value &&
            t.LoginProvider == ev.LoginProvider);

        if (tokenToRemove == null)
            return state;

        return state with
        {
            Tokens = state.Tokens.Remove(tokenToRemove),
            
        };
    }



    private static TokenState Given(TokenState state, TokenAggregateDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    /// <summary>
    /// Creates a new token aggregate for the specified user.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    /// <returns>A new <see cref="TokenAggregate"/> instance.</returns>
    public static TokenAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new TokenAggregateCreated());

        return aggregate;
    }

    /// <summary>
    /// Sets a token for the user, replacing any existing token with the same name and login provider.
    /// </summary>
    /// <param name="name">The name of the token. Cannot be empty.</param>
    /// <param name="value">The value of the token. Cannot be empty.</param>
    /// <param name="loginProvider">The login provider associated with this token, if any.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify tokens of a deleted user.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> or <paramref name="value"/> is empty.</exception>
    public void SetToken(
        TokenName name,
        TokenValue value,
        string loginProvider
        )
    {
        EnsureNotDeleted();
        

        // Validation
        if (string.IsNullOrEmpty(name.Value))
            throw new ArgumentException("Token name cannot be empty", nameof(name));

        if (string.IsNullOrEmpty(value.Value))
            throw new ArgumentException("Token value cannot be empty", nameof(value));

        AppendPendingChange(new TokenSet
        {
            Name = name,
            Value = value,
            LoginProvider = loginProvider ?? string.Empty,
            
        });
    }

    /// <summary>
    /// Removes a token for the user.
    /// </summary>
    /// <param name="name">The name of the token to remove.</param>
    /// <param name="loginProvider">The login provider associated with the token to remove.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to modify tokens of a deleted user.</exception>
    public void RemoveToken(
        TokenName name,
        string loginProvider
        )
    {
        EnsureNotDeleted();
        

        // Check if the token exists
        var tokenExists = State.Tokens.Any(t =>
            t.Name.Value == name.Value &&
            t.LoginProvider == (loginProvider ?? string.Empty));

        if (!tokenExists)
            return; // Token doesn't exist

        AppendPendingChange(new TokenRemoved
        {
            Name = name,
            LoginProvider = loginProvider ?? string.Empty,
            
        });
    }


    /// <summary>
    /// Marks the token aggregate as deleted.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted)
            return;



        AppendPendingChange(new TokenAggregateDeleted());
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify tokens of a deleted user");
    }

}