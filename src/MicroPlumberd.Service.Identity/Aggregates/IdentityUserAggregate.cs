namespace MicroPlumberd.Services.Identity.Aggregates;

/// <summary>
/// Aggregate root managing the identity and authentication aspects of a user including password, lockout, and two-factor authentication.
/// </summary>
[Aggregate]
[OutputStream("Identity")]
public partial class IdentityUserAggregate : AggregateBase<UserIdentifier, IdentityUserAggregate.IdentityUserState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityUserAggregate"/> class.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    public IdentityUserAggregate(UserIdentifier id) : base(id) { }

    /// <summary>
    /// Represents the state of an identity user including authentication and security settings.
    /// </summary>
    public readonly record struct IdentityUserState
    {
        /// <summary>
        /// Gets the hashed password for the user.
        /// </summary>
        public string PasswordHash { get; init; }

        /// <summary>
        /// Gets a value indicating whether two-factor authentication is enabled for this user.
        /// </summary>
        public bool TwoFactorEnabled { get; init; }

        /// <summary>
        /// Gets the authenticator key used for two-factor authentication.
        /// </summary>
        public string AuthenticatorKey { get; init; }

        /// <summary>
        /// Gets the number of failed access attempts for this user.
        /// </summary>
        public int AccessFailedCount { get; init; }

        /// <summary>
        /// Gets a value indicating whether lockout is enabled for this user.
        /// </summary>
        public bool LockoutEnabled { get; init; }

        /// <summary>
        /// Gets the date and time when the user's lockout ends. Null if not locked out.
        /// </summary>
        public DateTimeOffset? LockoutEnd { get; init; }

        /// <summary>
        /// Gets a value indicating whether this user has been deleted.
        /// </summary>
        public bool IsDeleted { get; init; }
    }

    // Event application methods
    private static IdentityUserState Given(IdentityUserState state, IdentityUserCreated ev)
    {
        return new IdentityUserState
        {
            PasswordHash = ev.PasswordHash,
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

    /// <summary>
    /// Creates a new identity user aggregate with the specified settings.
    /// </summary>
    /// <param name="id">The unique identifier for the user.</param>
    /// <param name="passwordHash">The hashed password for the user.</param>
    /// <param name="lockoutEnabled">Indicates whether lockout should be enabled for this user.</param>
    /// <returns>A new <see cref="IdentityUserAggregate"/> instance.</returns>
    public static IdentityUserAggregate Create(UserIdentifier id, string passwordHash, bool lockoutEnabled)
    {
        var aggregate = Empty(id);


        aggregate.AppendPendingChange(new IdentityUserCreated
        {
            PasswordHash = passwordHash,
            LockoutEnabled = lockoutEnabled,

        });

        return aggregate;
    }

    /// <summary>
    /// Changes the password hash for the user if it has actually changed.
    /// </summary>
    /// <param name="passwordHash">The new hashed password.</param>
    public void ChangePasswordHash(string passwordHash)
    {
        EnsureNotDeleted();

        // Only emit an event if the password hash has actually changed
        if (State.PasswordHash != passwordHash && !string.IsNullOrEmpty(passwordHash))
        {
            
            AppendPendingChange(new PasswordChanged
            {
                PasswordHash = passwordHash,
            });
        }
    }

    /// <summary>
    /// Changes the two-factor authentication enabled setting for the user.
    /// </summary>
    /// <param name="enabled">True to enable two-factor authentication; false to disable it.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to enable 2FA without an authenticator key.</exception>
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
                TwoFactorEnabled = enabled
            });
        }
    }

    /// <summary>
    /// Changes the authenticator key for two-factor authentication.
    /// </summary>
    /// <param name="authenticatorKey">The new authenticator key. Cannot be null or empty.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="authenticatorKey"/> is null or empty.</exception>
    public void ChangeAuthenticatorKey(string authenticatorKey )
    {
        EnsureNotDeleted();
        

        if (string.IsNullOrEmpty(authenticatorKey))
        {
            throw new ArgumentException("Authenticator key cannot be empty", nameof(authenticatorKey));
        }

        AppendPendingChange(new AuthenticatorKeyChanged
        {
            
            AuthenticatorKey = authenticatorKey,
            
        });
    }

    /// <summary>
    /// Increments the count of failed access attempts. If lockout is enabled and the threshold is reached, the user will be locked out.
    /// </summary>
    /// <returns>The new count of failed access attempts.</returns>
    public int IncrementAccessFailedCount()
    {
        EnsureNotDeleted();
        

        // If the user is locked out, don't increment the count
        if (IsLockedOut())
            return State.AccessFailedCount;

        var newCount = State.AccessFailedCount + 1;

        AppendPendingChange(new AccessFailedCountChanged
        {
            
            AccessFailedCount = newCount,
            
        });

        // If lockout is enabled and we've reached the threshold, lock the user out
        if (State.LockoutEnabled && newCount >= 5) // 5 is a common default
        {
            AppendPendingChange(new LockoutEndChanged
            {
                
                LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15), // 15 minutes is a common default
                
            });
        }

        return newCount;
    }

    /// <summary>
    /// Resets the count of failed access attempts to zero.
    /// </summary>
    public void ResetAccessFailedCount()
    {
        EnsureNotDeleted();
        

        if (State.AccessFailedCount > 0)
        {
            AppendPendingChange(new AccessFailedCountChanged
            {
                AccessFailedCount = 0,
            });
        }
    }

    /// <summary>
    /// Changes whether lockout is enabled for the user.
    /// </summary>
    /// <param name="enabled">True to enable lockout; false to disable it.</param>
    public void ChangeLockoutEnabled(bool enabled)
    {
        EnsureNotDeleted();

        // Only emit an event if the lockout enabled setting has actually changed
        if (State.LockoutEnabled != enabled)
        {
            AppendPendingChange(new LockoutEnabledChanged
            {
                LockoutEnabled = enabled
            });
        }
    }

    /// <summary>
    /// Changes the date and time when the user's lockout ends.
    /// </summary>
    /// <param name="lockoutEnd">The lockout end date and time. Must be in the future if not null.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="lockoutEnd"/> is not in the future.</exception>
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
                LockoutEnd = lockoutEnd
            });
        }
    }


    /// <summary>
    /// Marks the identity user as deleted.
    /// </summary>
    public void Delete()
    {
        if (State.IsDeleted)
            return;



        AppendPendingChange(new IdentityUserDeleted { });

    }

    /// <summary>
    /// Determines whether the user is currently locked out.
    /// </summary>
    /// <returns>True if the user is locked out; otherwise, false.</returns>
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