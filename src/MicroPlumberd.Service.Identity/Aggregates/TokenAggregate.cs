using Microsoft.AspNetCore.Identity;
using System.Collections.Immutable;

namespace MicroPlumberd.Service.Identity.Aggregates;

[Aggregate]
public partial class TokenAggregate : AggregateBase<UserIdentifier, TokenAggregate.TokenState>
{
    public TokenAggregate(UserIdentifier id) : base(id) { }

    public record TokenState
    {
        public UserIdentifier Id { get; init; }
        public ImmutableList<TokenRecord> Tokens { get; init; } = ImmutableList<TokenRecord>.Empty;
        public string ConcurrencyStamp { get; init; }
        public bool IsDeleted { get; init; }
    }

    public record TokenRecord
    {
        public TokenName Name { get; init; }
        public TokenValue Value { get; init; }
        public string LoginProvider { get; init; }
    }

    // Event application methods
    private static TokenState Given(TokenState state, TokenAggregateCreated ev)
    {
        
        return new TokenState
        {
            Id = ev.UserId,
            ConcurrencyStamp = ev.ConcurrencyStamp,
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
            ConcurrencyStamp = ev.ConcurrencyStamp
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
            ConcurrencyStamp = ev.ConcurrencyStamp
        };
    }

    private static TokenState Given(TokenState state, TokenConcurrencyStampChanged ev)
    {
        return state with { ConcurrencyStamp = ev.ConcurrencyStamp };
    }

    private static TokenState Given(TokenState state, TokenAggregateDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    // Command methods
    public static TokenAggregate Create(UserIdentifier id)
    {
        var aggregate = Empty(id);

        aggregate.AppendPendingChange(new TokenAggregateCreated
        {
            UserId = id,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });

        return aggregate;
    }

    public void SetToken(
        TokenName name,
        TokenValue value,
        string loginProvider,
        string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

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
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void RemoveToken(
        TokenName name,
        string loginProvider,
        string expectedConcurrencyStamp)
    {
        EnsureNotDeleted();
        ValidateConcurrencyStamp(expectedConcurrencyStamp);

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
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void UpdateConcurrencyStamp()
    {
        EnsureNotDeleted();

        AppendPendingChange(new TokenConcurrencyStampChanged
        {
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
    }

    public void Delete(string expectedConcurrencyStamp)
    {
        if (State.IsDeleted)
            return;

        ValidateConcurrencyStamp(expectedConcurrencyStamp);

        AppendPendingChange(new TokenAggregateDeleted());
    }

    // Helper methods
    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify tokens of a deleted user");
    }

    private void ValidateConcurrencyStamp(string expectedConcurrencyStamp)
    {
        if (expectedConcurrencyStamp != null &&
            State.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            throw new ConcurrencyException("User tokens were modified by another process");
        }
    }
}