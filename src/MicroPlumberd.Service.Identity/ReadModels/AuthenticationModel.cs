using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    /// <summary>
    /// Read model maintaining authentication data including passwords, lockout status, and two-factor settings.
    /// </summary>
    [EventHandler]
    [OutputStream("AuthenticationModel_v1")]
    public partial class AuthenticationModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, AuthenticationData> _authDataByUserId = new();

        /// <summary>
        /// Represents authentication data for a user.
        /// </summary>
        record AuthenticationData
        {
            /// <summary>
            /// Gets the hashed password.
            /// </summary>
            public string PasswordHash { get; init; }

            /// <summary>
            /// Gets a value indicating whether two-factor authentication is enabled.
            /// </summary>
            public bool TwoFactorEnabled { get; init; }

            /// <summary>
            /// Gets the authenticator key for two-factor authentication.
            /// </summary>
            public string AuthenticatorKey { get; init; }

            /// <summary>
            /// Gets the number of failed access attempts.
            /// </summary>
            public int AccessFailedCount { get; init; }

            /// <summary>
            /// Gets a value indicating whether lockout is enabled.
            /// </summary>
            public bool LockoutEnabled { get; init; }

            /// <summary>
            /// Gets the date and time when lockout ends.
            /// </summary>
            public DateTimeOffset? LockoutEnd { get; init; }
        }

        private async Task Given(Metadata m, IdentityUserCreated ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _authDataByUserId[userId] = new AuthenticationData
            {
                PasswordHash = ev.PasswordHash,
                //SecurityStamp = ev.SecurityStamp,
                TwoFactorEnabled = false,
                AuthenticatorKey = null,
                AccessFailedCount = 0,
                LockoutEnabled = ev.LockoutEnabled,
                LockoutEnd = null
            };

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PasswordChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                _authDataByUserId[userId] = data with
                {
                    PasswordHash = ev.PasswordHash,
                    //SecurityStamp = ev.SecurityStamp
                };
            }

            await Task.CompletedTask;
        }

      

        private async Task Given(Metadata m, TwoFactorChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                var newData = data with
                {
                    TwoFactorEnabled = ev.TwoFactorEnabled
                };

                // If disabling 2FA, clear the authenticator key
                if (!ev.TwoFactorEnabled)
                {
                    newData = newData with { AuthenticatorKey = null };
                }

                _authDataByUserId[userId] = newData;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, AuthenticatorKeyChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                _authDataByUserId[userId] = data with
                {
                    AuthenticatorKey = ev.AuthenticatorKey
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, AccessFailedCountChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                _authDataByUserId[userId] = data with
                {
                    AccessFailedCount = ev.AccessFailedCount
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEnabledChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                _authDataByUserId[userId] = data with
                {
                    LockoutEnabled = ev.LockoutEnabled
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, LockoutEndChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_authDataByUserId.TryGetValue(userId, out var data))
            {
                _authDataByUserId[userId] = data with
                {
                    LockoutEnd = ev.LockoutEnd
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, IdentityUserDeleted ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            _authDataByUserId.TryRemove(userId, out _);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the authenticator key for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>The authenticator key, or null if not found.</returns>
        public string GetAuthenticationDataKey(UserIdentifier userId)
        {
            _authDataByUserId.TryGetValue(userId, out var data);
            return data?.AuthenticatorKey;
        }
    }
}