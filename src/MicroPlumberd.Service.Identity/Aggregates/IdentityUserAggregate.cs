namespace MicroPlumberd.Services.Identity.Aggregates;

[Aggregate]
public partial class IdentityUserAggregate : AggregateBase<UserIdentifier, IdentityUserAggregate.IdentityUserState>
{
    public IdentityUserAggregate(UserIdentifier id) : base(id) { }

    public record IdentityUserState
    {
        public UserIdentifier Id { get; init; }
        public string PasswordHash { get; init; }
        public string SecurityStamp { get; init; }
        public bool TwoFactorEnabled { get; init; }
        public string AuthenticatorKey { get; init; }
        public int AccessFailedCount { get; init; }
        public bool LockoutEnabled { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        
        public bool IsDeleted { get; init; }
    }

    // Event application methods
    private static IdentityUserState Given(IdentityUserState state, IdentityUserCreated ev)
    {
        return new IdentityUserState
        {
            Id = ev.UserId,
            PasswordHash = ev.PasswordHash,
            SecurityStamp = ev.SecurityStamp,
            TwoFactorEnabled = false,
            AuthenticatorKey = null,
            AccessFailedCount = 0,
            LockoutEnabled = ev.LockoutEnabled,
            LockoutEnd = null,
            
            IsDeleted = false
        };
    }

    private static IdentityUserState Given(IdentityUserState state, PasswordChanged ev)
    {
        return state with
        {
            PasswordHash = ev.PasswordHash,
            SecurityStamp = ev.SecurityStamp,
            
        };
    }

    private static IdentityUserState Given(IdentityUserState state, SecurityStampChanged ev)
    {
        return state with
        {
            SecurityStamp = ev.SecurityStamp,
            
        };
    }

    private static IdentityUserState Given(IdentityUserState state, TwoFactorChanged ev)
    {
        var newState = state with
        {
            TwoFactorEnabled = ev.TwoFactorEnabled,
            
        };

        // If disabling 2FA, clear the authenticator key
        if (!ev.TwoFactorEnabled)
        {
            newState = newState with { AuthenticatorKey = null };
        }

        return newState;
    }

    private static IdentityUserState Given(IdentityUserState state, AuthenticatorKeyChanged ev)
    {
        return state with
        {
            AuthenticatorKey = ev.AuthenticatorKey,
            
        };
    }

    private static IdentityUserState Given(IdentityUserState state, AccessFailedCountChanged ev)
    {
        return state with
        {
            AccessFailedCount = ev.AccessFailedCount,
            
        };
    }

    private static IdentityUserState Given(IdentityUserState state, LockoutEnabledChanged ev)
    {
        return state with
        {
            LockoutEnabled = ev.LockoutEnabled,
            
        };
    }

    private static IdentityUserState Given(IdentityUserState state, LockoutEndChanged ev)
    {
        return state with
        {
            LockoutEnd = ev.LockoutEnd,
            
        };
    }



    private static IdentityUserState Given(IdentityUserState state, IdentityUserDeleted ev)
    {
        return state with { IsDeleted = true };
    }

    // Command methods
    public static IdentityUserAggregate Create(UserIdentifier id, string passwordHash, bool lockoutEnabled)
    {
        var aggregate = Empty(id);

        // Generate a new security stamp
        var securityStamp = Guid.NewGuid().ToString();

        aggregate.AppendPendingChange(new IdentityUserCreated
        {
            Id = Guid.NewGuid(),
            UserId = id,
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            LockoutEnabled = lockoutEnabled,
            
        });

        return aggregate;
    }

    /// <summary>
    /// Updates the password hash if it has changed
    /// </summary>
    public void ChangePasswordHash(string passwordHash)
    {
        EnsureNotDeleted();

        // Only emit an event if the password hash has actually changed
        if (State.PasswordHash != passwordHash && !string.IsNullOrEmpty(passwordHash))
        {
            // Generate a new security stamp whenever the password changes
            var securityStamp = Guid.NewGuid().ToString();

            AppendPendingChange(new PasswordChanged
            {
                Id = Guid.NewGuid(),
                PasswordHash = passwordHash,
                SecurityStamp = securityStamp
            });
        }
    }

    public void ChangeSecurityStamp()
    {
        EnsureNotDeleted();
        

        AppendPendingChange(new SecurityStampChanged
        {
            Id = Guid.NewGuid(),
            SecurityStamp = Guid.NewGuid().ToString(),
            
        });
    }

    /// <summary>
    /// Updates the two-factor enabled setting if it has changed
    /// </summary>
    public void ChangeTwoFactorEnabled(bool enabled)
    {
        EnsureNotDeleted();

        // Only emit an event if the two-factor enabled setting has actually changed
        if (State.TwoFactorEnabled != enabled)
        {
            // If enabling 2FA, ensure we have an authenticator key
            if (enabled && string.IsNullOrEmpty(State.AuthenticatorKey))
            {
                throw new InvalidOperationException("Cannot enable two-factor authentication without an authenticator key");
            }

            AppendPendingChange(new TwoFactorChanged
            {
                Id = Guid.NewGuid(),
                TwoFactorEnabled = enabled
            });
        }
    }

    public void ChangeAuthenticatorKey(string authenticatorKey )
    {
        EnsureNotDeleted();
        

        if (string.IsNullOrEmpty(authenticatorKey))
        {
            throw new ArgumentException("Authenticator key cannot be empty", nameof(authenticatorKey));
        }

        AppendPendingChange(new AuthenticatorKeyChanged
        {
            Id = Guid.NewGuid(),
            AuthenticatorKey = authenticatorKey,
            
        });
    }

    public int IncrementAccessFailedCount()
    {
        EnsureNotDeleted();
        

        // If the user is locked out, don't increment the count
        if (IsLockedOut())
            return State.AccessFailedCount;

        var newCount = State.AccessFailedCount + 1;

        AppendPendingChange(new AccessFailedCountChanged
        {
            Id = Guid.NewGuid(),
            AccessFailedCount = newCount,
            
        });

        // If lockout is enabled and we've reached the threshold, lock the user out
        if (State.LockoutEnabled && newCount >= 5) // 5 is a common default
        {
            AppendPendingChange(new LockoutEndChanged
            {
                Id = Guid.NewGuid(),
                LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15), // 15 minutes is a common default
                
            });
        }

        return newCount;
    }

    public void ResetAccessFailedCount()
    {
        EnsureNotDeleted();
        

        if (State.AccessFailedCount > 0)
        {
            AppendPendingChange(new AccessFailedCountChanged
            {
                Id = Guid.NewGuid(),
                AccessFailedCount = 0,
                
            });
        }
    }

    /// <summary>
    /// Updates the lockout enabled setting if it has changed
    /// </summary>
    public void ChangeLockoutEnabled(bool enabled)
    {
        EnsureNotDeleted();

        // Only emit an event if the lockout enabled setting has actually changed
        if (State.LockoutEnabled != enabled)
        {
            AppendPendingChange(new LockoutEnabledChanged
            {
                Id = Guid.NewGuid(),
                LockoutEnabled = enabled
            });
        }
    }

    public void ChangeLockoutEnd(DateTimeOffset? lockoutEnd )
    {
        EnsureNotDeleted();


        // If setting a lockout end, ensure it's in the future
        if (State.LockoutEnd != lockoutEnd)
        {
            // If setting a lockout end, ensure it's in the future
            if (lockoutEnd.HasValue && lockoutEnd.Value <= DateTimeOffset.UtcNow)
            {
                throw new ArgumentException("Lockout end date must be in the future", nameof(lockoutEnd));
            }

            AppendPendingChange(new LockoutEndChanged
            {
                Id = Guid.NewGuid(),
                LockoutEnd = lockoutEnd
            });
        }
    }


    public void Delete()
    {
        if (State.IsDeleted)
            return;

        

        AppendPendingChange(new IdentityUserDeleted
        {
            Id = Guid.NewGuid()
        });
    }

    // Helper methods
    public bool IsLockedOut()
    {
        return State.LockoutEnabled &&
               State.LockoutEnd.HasValue &&
               State.LockoutEnd.Value > DateTimeOffset.UtcNow;
    }

    private void EnsureNotDeleted()
    {
        if (State.IsDeleted)
            throw new InvalidOperationException("Cannot modify a deleted user");
    }


}