using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services.Identity.Aggregates;


namespace MicroPlumberd.Services.Identity.ReadModels
{
    /// <summary>
    /// Read model maintaining user profile data with efficient lookups by username and email.
    /// </summary>
    [EventHandler]
    [OutputStream("UserProfileModel_v1")]
    public partial class UserProfileModel
    {
        private readonly ConcurrentDictionary<UserIdentifier, ProfileData> _profilesById = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedName = new();
        private readonly ConcurrentDictionary<string, UserIdentifier> _userIdsByNormalizedEmail = new();

        /// <summary>
        /// Represents user profile data in the read model.
        /// </summary>
        public record ProfileData
        {
            /// <summary>
            /// Gets the username.
            /// </summary>
            public string UserName { get; init; }

            /// <summary>
            /// Gets the normalized username for case-insensitive lookups.
            /// </summary>
            public string NormalizedUserName { get; init; }

            /// <summary>
            /// Gets the email address.
            /// </summary>
            public string Email { get; init; }

            /// <summary>
            /// Gets the normalized email address for case-insensitive lookups.
            /// </summary>
            public string NormalizedEmail { get; init; }

            /// <summary>
            /// Gets a value indicating whether the email has been confirmed.
            /// </summary>
            public bool EmailConfirmed { get; init; }

            /// <summary>
            /// Gets the phone number.
            /// </summary>
            public string PhoneNumber { get; init; }

            /// <summary>
            /// Gets a value indicating whether the phone number has been confirmed.
            /// </summary>
            public bool PhoneNumberConfirmed { get; init; }

        }

        private async Task Given(Metadata m, UserProfileCreated ev)
        {
            var userId = m.StreamId<UserIdentifier>();
            var profile = new ProfileData
            {
                UserName = ev.UserName,
                NormalizedUserName = ev.NormalizedUserName,
                Email = ev.Email,
                NormalizedEmail = ev.NormalizedEmail,
                EmailConfirmed = false,
                PhoneNumber = ev.PhoneNumber,
                PhoneNumberConfirmed = false,
                
            };
            
            _profilesById[userId] = profile;
            _userIdsByNormalizedName[ev.NormalizedUserName] = userId;

            if (!string.IsNullOrEmpty(ev.NormalizedEmail))
            {
                _userIdsByNormalizedEmail[ev.NormalizedEmail] = userId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, UserNameChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                // Remove old normalized name mapping
                _userIdsByNormalizedName.TryRemove(profile.NormalizedUserName, out _);

                // Update profile
                var updatedProfile = profile with
                {
                    UserName = ev.UserName,
                    NormalizedUserName = ev.NormalizedUserName,
                    
                };

                _profilesById[userId] = updatedProfile;
                _userIdsByNormalizedName[ev.NormalizedUserName] = userId;
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                // Remove old normalized email mapping
                if (!string.IsNullOrEmpty(profile.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail.TryRemove(profile.NormalizedEmail, out _);
                }

                // Update profile
                var updatedProfile = profile with
                {
                    Email = ev.Email,
                    NormalizedEmail = ev.NormalizedEmail,
                    EmailConfirmed = false,
                    
                };

                _profilesById[userId] = updatedProfile;

                // Add new normalized email mapping
                if (!string.IsNullOrEmpty(ev.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail[ev.NormalizedEmail] = userId;
                }
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, EmailConfirmed ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    EmailConfirmed = true,
                    
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberChanged ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    PhoneNumber = ev.PhoneNumber,
                    PhoneNumberConfirmed = false,
                    
                };
            }

            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, PhoneNumberConfirmed ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryGetValue(userId, out var profile))
            {
                _profilesById[userId] = profile with
                {
                    PhoneNumberConfirmed = true,
                    
                };
            }

            await Task.CompletedTask;
        }


        private async Task Given(Metadata m, UserProfileDeleted ev)
        {
            var userId = m.StreamId<UserIdentifier>();

            if (_profilesById.TryRemove(userId, out var profile))
            {
                _userIdsByNormalizedName.TryRemove(profile.NormalizedUserName, out _);

                if (!string.IsNullOrEmpty(profile.NormalizedEmail))
                {
                    _userIdsByNormalizedEmail.TryRemove(profile.NormalizedEmail, out _);
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets a user profile by user ID.
        /// </summary>
        /// <param name="id">The user identifier.</param>
        /// <returns>The profile data, or null if not found.</returns>
        public ProfileData GetById(UserIdentifier id)
        {
            _profilesById.TryGetValue(id, out var profile);
            return profile;
        }

        /// <summary>
        /// Gets a user ID by normalized username.
        /// </summary>
        /// <param name="normalizedUserName">The normalized username to lookup.</param>
        /// <returns>The user identifier, or default if not found.</returns>
        public UserIdentifier GetIdByNormalizedUserName(string normalizedUserName)
        {
            _userIdsByNormalizedName.TryGetValue(normalizedUserName, out var id);
            return id;
        }

        /// <summary>
        /// Gets a user ID by normalized email.
        /// </summary>
        /// <param name="normalizedEmail">The normalized email to lookup.</param>
        /// <returns>The user identifier, or default if not found.</returns>
        public UserIdentifier GetIdByNormalizedEmail(string normalizedEmail)
        {
            _userIdsByNormalizedEmail.TryGetValue(normalizedEmail, out var id);
            return id;
        }

        /// <summary>
        /// Gets all user profiles.
        /// </summary>
        /// <returns>An immutable list of all profile data.</returns>
        public ImmutableList<ProfileData> GetAllProfiles()
        {
            return _profilesById.Values.ToImmutableList();
        }
    }
}